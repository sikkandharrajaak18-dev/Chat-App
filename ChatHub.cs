using Chat_App;
using Chat_App.Models;
using Chat_App.Services;
using Chat_App.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Security.Claims;
[Authorize]
public class ChatHub : Hub
{
    private readonly ApplicationDBContext _db;
    private readonly IDatabase _redis;
    private readonly WebPushService _webPush;
    public ChatHub(ApplicationDBContext db, IConnectionMultiplexer redis, WebPushService webPush)
    {
        _db = db;
        _redis = redis.GetDatabase();
        _webPush = webPush;
    }
    private async Task SendPushNotification(int userId, string title, string body, string url)
    {
        await _webPush.SendNotification(userId, title, body, "/chatapp.png", url);
    }
    private async Task DeleteMessageCache(int user1, int user2)
    {
        var key1 = $"messages:{user1}:{user2}";
        var key2 = $"messages:{user2}:{user1}";
        await _redis.KeyDeleteAsync(new RedisKey[] { key1, key2 });
    }
    public async Task RegisterUser(int userId)
    {
        var authenticatedUserId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (userId != authenticatedUserId)
            return;
        var wasOnline = ConnectionManager.IsOnline(userId);
        ConnectionManager.AddConnection(userId, Context.ConnectionId);
        var user = await _db.user.FindAsync(userId);
        if (user != null)
        {
            user.IsOnline = true;
            user.LastSeen = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            if (!wasOnline)
            {
                var undeliveredMessages = _db.message
                    .Where(m => m.ReceiverId == userId && !m.IsDelivered && !m.BlockedStatus)
                    .ToList();
                foreach (var msg in undeliveredMessages)
                    msg.IsDelivered = true;
                await _db.SaveChangesAsync();
                var affectedSenders = undeliveredMessages.Select(m => m.SenderId).Distinct();
                foreach (var senderId in affectedSenders)
                    await DeleteMessageCache(senderId, userId);
                foreach (var msg in undeliveredMessages)
                {
                    foreach (var connId in ConnectionManager.GetConnections(msg.SenderId))
                        await Clients.Client(connId).SendAsync("MessageDelivered", msg.MessageId);
                }
                var undeliveredGroupRecipients = _db.groupMessageRecipient
                    .Where(r => r.UserId == userId && !r.IsDelivered)
                    .ToList();
                foreach (var recipient in undeliveredGroupRecipients)
                {
                    recipient.IsDelivered = true;
                    recipient.DeliveredAt = DateTime.UtcNow;
                }
                await _db.SaveChangesAsync();
                var affectedGroupMsgIds = undeliveredGroupRecipients.Select(r => r.GroupMessageId).Distinct();
                foreach (var msgId in affectedGroupMsgIds)
                {
                    var msg = await _db.groupMessage.FindAsync(msgId);
                    if (msg == null) continue;
                    var group = await _db.group.FindAsync(msg.GroupId);
                    if (group?.UserIds == null) continue;
                    var totalRecipients = group.UserIds.Count - 1;
                    var deliveredCount = await _db.groupMessageRecipient
                        .CountAsync(r => r.GroupMessageId == msgId && r.IsDelivered);
                    var readCount = await _db.groupMessageRecipient
                        .CountAsync(r => r.GroupMessageId == msgId && r.IsRead);
                    foreach (var connId in ConnectionManager.GetConnections(msg.SenderId))
                    {
                        await Clients.Client(connId).SendAsync("GroupMessageStatusUpdated",
                            msgId, msg.GroupId, totalRecipients, deliveredCount, readCount);
                    }
                }
            }
            await Clients.All.SendAsync("UserStatusChanged", userId, true, DateTime.UtcNow.ToString("o"));
        }
    }
    public async Task SendMessage(int senderId, int receiverId, string message)
    {
        senderId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));

        var receiver = await _db.user.FindAsync(receiverId);
        var sender = await _db.user.FindAsync(senderId);

        // Skip friendship check if either side is Admin
        bool senderIsAdmin = sender?.Role == "Admin";
        bool receiverIsAdmin = receiver?.Role == "Admin";

        if (!senderIsAdmin && !receiverIsAdmin)
        {
            var friendship = await _db.friendRequests
                .FirstOrDefaultAsync(f =>
                    (f.SenderId == senderId && f.ReceiverId == receiverId) ||
                    (f.SenderId == receiverId && f.ReceiverId == senderId));

            if (friendship == null || friendship.Status != "Accepted")
            {
                foreach (var connId in ConnectionManager.GetConnections(senderId))
                    await Clients.Client(connId).SendAsync("ReceiveMessage", senderId, message, -1);
                return;
            }
        }

        var isBlocked = receiver?.BlockedUsers != null
            && receiver.BlockedUsers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(senderId.ToString());

        var msg = new Message
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Text = message,
            SentAt = DateTime.UtcNow,
            BlockedStatus = isBlocked
        };
        _db.message.Add(msg);
        await _db.SaveChangesAsync();
        await DeleteMessageCache(senderId, receiverId);

        if (isBlocked)
        {
            foreach (var connId in ConnectionManager.GetConnections(senderId))
                await Clients.Client(connId).SendAsync("ReceiveMessage", senderId, message, msg.MessageId);
            return;
        }

        var receiverConnections = ConnectionManager.GetConnections(receiverId).ToList();
        if (receiverConnections.Any())
        {
            try
            {
                msg.IsDelivered = true;
                await _db.SaveChangesAsync();
                foreach (var connId in receiverConnections)
                    await Clients.Client(connId).SendAsync("ReceiveMessage", senderId, message, msg.MessageId);
                foreach (var connId in ConnectionManager.GetConnections(senderId))
                {
                    await Clients.Client(connId).SendAsync("ReceiveMessage", senderId, message, msg.MessageId);
                    await Clients.Client(connId).SendAsync("MessageDelivered", msg.MessageId);
                }
            }
            catch { }
        }
        else
        {
            if (sender != null)
            {
                foreach (var connId in ConnectionManager.GetConnections(senderId))
                    await Clients.Client(connId).SendAsync("ReceiveMessage", senderId, message, msg.MessageId);
                await SendPushNotification(receiverId, sender.username, message, $"/Chat/Chat/{senderId}");
            }
        }
    }
    public async Task SendFileMessage(int senderId, int receiverId, string filePath, string fileType, string fileName, double? duration = null)
    {
        senderId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));

        var receiver = await _db.user.FindAsync(receiverId);
        var sender = await _db.user.FindAsync(senderId);

        bool senderIsAdmin = sender?.Role == "Admin";
        bool receiverIsAdmin = receiver?.Role == "Admin";

        if (!senderIsAdmin && !receiverIsAdmin)
        {
            var friendship = await _db.friendRequests
                .FirstOrDefaultAsync(f =>
                    (f.SenderId == senderId && f.ReceiverId == receiverId) ||
                    (f.SenderId == receiverId && f.ReceiverId == senderId));

            if (friendship == null || friendship.Status != "Accepted")
                return;
        }
        var isBlocked = receiver?.BlockedUsers != null
            && receiver.BlockedUsers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(senderId.ToString());
        var msg = new Message
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Text = filePath,
            SentAt = DateTime.UtcNow,
            FileType = fileType,
            FileName = fileName,
            Duration = duration,
            BlockedStatus = isBlocked
        };
        _db.message.Add(msg);
        await _db.SaveChangesAsync();
        await DeleteMessageCache(senderId, receiverId);
        if (isBlocked)
        {
            foreach (var connId in ConnectionManager.GetConnections(senderId))
                await Clients.Client(connId).SendAsync("ReceiveFileMessage", senderId, filePath, msg.MessageId, fileType, fileName, duration);
            return;
        }
        var receiverConnections = ConnectionManager.GetConnections(receiverId).ToList();
        if (receiverConnections.Any())
        {
            try
            {
                msg.IsDelivered = true;
                await _db.SaveChangesAsync();
                foreach (var connId in receiverConnections)
                    await Clients.Client(connId).SendAsync("ReceiveFileMessage", senderId, filePath, msg.MessageId, fileType, fileName, duration);
                foreach (var connId in ConnectionManager.GetConnections(senderId))
                {
                    await Clients.Client(connId).SendAsync("ReceiveFileMessage", senderId, filePath, msg.MessageId, fileType, fileName, duration);
                    await Clients.Client(connId).SendAsync("MessageDelivered", msg.MessageId);
                }
            }
            catch { }
        }
        else
        {
           
            if (sender != null)
            {
                foreach (var connId in ConnectionManager.GetConnections(senderId))
                    await Clients.Client(connId).SendAsync("ReceiveFileMessage", senderId, filePath, msg.MessageId, fileType, fileName, duration);
                var body = fileType == "image" ? "sent a photo"
                    : fileType == "video" ? "sent a video"
                    : fileType == "audio" ? "sent a voice message"
                    : "sent a document";
                await SendPushNotification(receiverId, sender.username, body, $"/Chat/Chat/{senderId}");
            }
        }
    }
    public async Task MarkMessagesAsRead(int senderId, int receiverId)
    {
        var unreadMessages = _db.message
            .Where(m => m.SenderId == senderId && m.ReceiverId == receiverId && !m.IsRead && !m.BlockedStatus)
            .ToList();
        foreach (var msg in unreadMessages)
            msg.IsRead = true;
        await _db.SaveChangesAsync();
        await DeleteMessageCache(senderId, receiverId);
        foreach (var connId in ConnectionManager.GetConnections(senderId))
        {
            foreach (var msg in unreadMessages)
                await Clients.Client(connId).SendAsync("MessageRead", msg.MessageId);
        }
    }
    private async Task DeleteGroupMessageCache(int groupId)
    {
        await _redis.KeyDeleteAsync($"messages:group:{groupId}");
    }
    public async Task SendGroupMessage(int senderId, int groupId, string message)
    {
        var sender = await _db.user.FindAsync(senderId);
        if (sender == null) return;
        var msg = new GroupMessage
        {
            GroupId = groupId,
            SenderId = senderId,
            SenderName = sender.username,
            SenderProfileImage = sender.ProfileImagePath,
            Text = message,
            SentAt = DateTime.UtcNow
        };
        _db.groupMessage.Add(msg);
        await _db.SaveChangesAsync();
        await DeleteGroupMessageCache(groupId);
        var group = await _db.group.FindAsync(groupId);
        if (group?.UserIds == null) return;
        var sendTasks = new List<Task>();
        foreach (var memberId in group.UserIds)
        {
            if (memberId == senderId) continue;
            var recipient = new GroupMessageRecipient
            {
                GroupMessageId = msg.GroupMessageId,
                UserId = memberId
            };
            _db.groupMessageRecipient.Add(recipient);
            var memberConnections = ConnectionManager.GetConnections(memberId).ToList();
            if (memberConnections.Any())
            {
                foreach (var connId in memberConnections)
                {
                    sendTasks.Add(Clients.Client(connId).SendAsync("ReceiveGroupMessage",
                        senderId, sender.username, sender.ProfileImagePath, groupId,
                        message, msg.GroupMessageId, msg.SentAt));
                }
            }
            else
            {
                sendTasks.Add(SendPushNotification(memberId, group.GroupName, $"{sender.username}: {message}", $"/Chat/GroupChat/{groupId}"));
            }
        }
        await Task.WhenAll(sendTasks);
        await _db.SaveChangesAsync();
        var totalRecipients = group.UserIds.Count - 1;
        foreach (var connId in ConnectionManager.GetConnections(senderId))
        {
            await Clients.Client(connId).SendAsync("ReceiveGroupMessage",
                senderId, sender.username, sender.ProfileImagePath, groupId,
                message, msg.GroupMessageId, msg.SentAt);
            await Clients.Client(connId).SendAsync("GroupMessageStatusUpdated",
                msg.GroupMessageId, groupId, totalRecipients, 0, 0);
        }
    }
    public async Task SendGroupFileMessage(int senderId, int groupId, string filePath, string fileType, string fileName, double? duration = null)
    {
        var sender = await _db.user.FindAsync(senderId);
        if (sender == null) return;
        var msg = new GroupMessage
        {
            GroupId = groupId,
            SenderId = senderId,
            SenderName = sender.username,
            SenderProfileImage = sender.ProfileImagePath,
            Text = filePath,
            SentAt = DateTime.UtcNow,
            FileType = fileType,
            FileName = fileName,
            Duration = duration
        };
        _db.groupMessage.Add(msg);
        await _db.SaveChangesAsync();
        await DeleteGroupMessageCache(groupId);
        var group = await _db.group.FindAsync(groupId);
        if (group?.UserIds == null) return;
        var sendTasks = new List<Task>();
        foreach (var memberId in group.UserIds)
        {
            if (memberId == senderId) continue;
            var recipient = new GroupMessageRecipient
            {
                GroupMessageId = msg.GroupMessageId,
                UserId = memberId
            };
            _db.groupMessageRecipient.Add(recipient);
            var memberConnections = ConnectionManager.GetConnections(memberId).ToList();
            if (memberConnections.Any())
            {
                foreach (var connId in memberConnections)
                {
                    sendTasks.Add(Clients.Client(connId).SendAsync("ReceiveGroupFileMessage",
                        senderId, sender.username, sender.ProfileImagePath, groupId,
                        filePath, msg.GroupMessageId, fileType, fileName, msg.SentAt, duration));
                }
            }
            else
            {
                var body = fileType == "image" ? $"{sender.username} sent a photo"
                    : fileType == "video" ? $"{sender.username} sent a video"
                    : fileType == "audio" ? $"{sender.username} sent a voice message"
                    : $"{sender.username} sent a document";
                sendTasks.Add(SendPushNotification(memberId, group.GroupName, body, $"/Chat/GroupChat/{groupId}"));
            }
        }
        await Task.WhenAll(sendTasks);
        await _db.SaveChangesAsync();
        var totalRecipients = group.UserIds.Count - 1;
        foreach (var connId in ConnectionManager.GetConnections(senderId))
        {
            await Clients.Client(connId).SendAsync("ReceiveGroupFileMessage",
                senderId, sender.username, sender.ProfileImagePath, groupId,
                filePath, msg.GroupMessageId, fileType, fileName, msg.SentAt, duration);
            await Clients.Client(connId).SendAsync("GroupMessageStatusUpdated",
                msg.GroupMessageId, groupId, totalRecipients, 0, 0);
        }
    }
    public async Task CallUser(int receiverId, string type)
    {
        var callerId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        var caller = await _db.user.FindAsync(callerId);
        if (caller == null) return;
        foreach (var connId in ConnectionManager.GetConnections(receiverId))
        {
            await Clients.Client(connId).SendAsync("IncomingCall",
                callerId,
                caller.Name ?? caller.username,
                caller.ProfileImagePath ?? "",
                type);
        }
    }
    public async Task AnswerCall(int callerId, bool accept)
    {
        var responderId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        var responder = await _db.user.FindAsync(responderId);
        if (responder == null) return;
        foreach (var connId in ConnectionManager.GetConnections(callerId))
        {
            if (accept)
                await Clients.Client(connId).SendAsync("CallAccepted", responderId, responder.Name ?? responder.username);
            else
                await Clients.Client(connId).SendAsync("CallDeclined", responderId, "declined");
        }
    }
    public async Task SendOffer(int receiverId, string sdp)
    {
        foreach (var connId in ConnectionManager.GetConnections(receiverId))
            await Clients.Client(connId).SendAsync("ReceiveOffer", sdp);
    }
    public async Task SendAnswer(int callerId, string sdp)
    {
        foreach (var connId in ConnectionManager.GetConnections(callerId))
            await Clients.Client(connId).SendAsync("ReceiveAnswer", sdp);
    }
    public async Task SendIceCandidate(int receiverId, string candidate)
    {
        foreach (var connId in ConnectionManager.GetConnections(receiverId))
            await Clients.Client(connId).SendAsync("ReceiveIceCandidate", candidate);
    }
    public async Task EndCall(int receiverId)
    {
        var senderId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        foreach (var connId in ConnectionManager.GetConnections(receiverId))
            await Clients.Client(connId).SendAsync("RemoteHangup", senderId);
    }
    public async Task DeleteGroupMessage(int messageId)
    {
        var msg = await _db.groupMessage.FindAsync(messageId);
        if (msg == null) return;
        msg.DeletedStatus = "EveryOne";
        await _db.SaveChangesAsync();
        await DeleteGroupMessageCache(msg.GroupId);
        var group = await _db.group.FindAsync(msg.GroupId);
        if (group?.UserIds == null) return;
        foreach (var memberId in group.UserIds)
        {
            foreach (var connId in ConnectionManager.GetConnections(memberId))
            {
                try
                {
                    await Clients.Client(connId).SendAsync("GroupMessageDeleted", messageId, msg.GroupId);
                }
                catch { }
            }
        }
    }
    public async Task MarkGroupMessageDelivered(int messageId, int groupId, int userId)
    {
        var authenticatedUserId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (userId != authenticatedUserId) return;
        var recipient = await _db.groupMessageRecipient
            .FirstOrDefaultAsync(r => r.GroupMessageId == messageId && r.UserId == userId);
        if (recipient == null || recipient.IsDelivered) return;
        recipient.IsDelivered = true;
        recipient.DeliveredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var group = await _db.group.FindAsync(groupId);
        if (group?.UserIds == null) return;
        var totalRecipients = group.UserIds.Count - 1;
        var deliveredCount = await _db.groupMessageRecipient
            .CountAsync(r => r.GroupMessageId == messageId && r.IsDelivered);
        var readCount = await _db.groupMessageRecipient
            .CountAsync(r => r.GroupMessageId == messageId && r.IsRead);
        var msg = await _db.groupMessage.FindAsync(messageId);
        if (msg == null) return;
        foreach (var connId in ConnectionManager.GetConnections(msg.SenderId))
        {
            await Clients.Client(connId).SendAsync("GroupMessageStatusUpdated",
                messageId, groupId, totalRecipients, deliveredCount, readCount);
        }
    }
    public async Task MarkGroupMessageRead(int messageId, int groupId, int userId)
    {
        var authenticatedUserId = int.Parse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (userId != authenticatedUserId) return;
        var recipient = await _db.groupMessageRecipient
            .FirstOrDefaultAsync(r => r.GroupMessageId == messageId && r.UserId == userId);
        if (recipient == null || recipient.IsRead) return;
        recipient.IsRead = true;
        recipient.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var group = await _db.group.FindAsync(groupId);
        if (group?.UserIds == null) return;
        var totalRecipients = group.UserIds.Count - 1;
        var deliveredCount = await _db.groupMessageRecipient
            .CountAsync(r => r.GroupMessageId == messageId && r.IsDelivered);
        var readCount = await _db.groupMessageRecipient
            .CountAsync(r => r.GroupMessageId == messageId && r.IsRead);
        var msg = await _db.groupMessage.FindAsync(messageId);
        if (msg == null) return;
        foreach (var connId in ConnectionManager.GetConnections(msg.SenderId))
        {
            await Clients.Client(connId).SendAsync("GroupMessageStatusUpdated",
                messageId, groupId, totalRecipients, deliveredCount, readCount);
        }
    }
    public async Task GetUserStatus(int userId)
    {
        var user = await _db.user.FindAsync(userId);
        if (user != null)
        {
            var lastSeenStr = user.LastSeen.HasValue
                ? DateTime.SpecifyKind(user.LastSeen.Value, DateTimeKind.Utc).ToString("o")
                : null;
            await Clients.Caller.SendAsync("UserStatusResponse", userId, user.IsOnline, lastSeenStr);
        }
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        int? userId = ConnectionManager.RemoveConnection(Context.ConnectionId);
        if (userId.HasValue)
        {
            bool stillOnline = ConnectionManager.IsOnline(userId.Value);
            if (!stillOnline)
            {
                var dbUser = await _db.user.FindAsync(userId.Value);
                if (dbUser != null)
                {
                    dbUser.IsOnline = false;
                    dbUser.LastSeen = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    await Clients.All.SendAsync("UserStatusChanged", userId.Value, false, DateTime.UtcNow.ToString("o"));
                }
            }
        }
        await base.OnDisconnectedAsync(exception);
    }
}
