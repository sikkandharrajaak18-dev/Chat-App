using Chat_App.Models;
using Chat_App.Repositories;
using Chat_App.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace Chat_App.Services.Implementations
{
    public class ChatIndexService : IChatIndexService
    {
        private readonly IUserRepository _userRepo;
        private readonly IMessageRepository _messageRepo;
        private readonly IGroupRepository _groupRepo;
        private readonly IGroupMessageRepository _groupMsgRepo;
        private readonly IGroupMessageRecipientRepository _groupMsgRecipientRepo;
        private readonly IHiddenRepository _hiddenRepo;
        private readonly IFriendRequestRepository _friendRequestRepo;
        public ChatIndexService(
            IUserRepository userRepo,
            IMessageRepository messageRepo,
            IGroupRepository groupRepo,
            IGroupMessageRepository groupMsgRepo,
            IGroupMessageRecipientRepository groupMsgRecipientRepo,
            IHiddenRepository hiddenRepo,
            IFriendRequestRepository friendRequestRepo)
        {
            _userRepo = userRepo;
            _messageRepo = messageRepo;
            _groupRepo = groupRepo;
            _groupMsgRepo = groupMsgRepo;
            _groupMsgRecipientRepo = groupMsgRecipientRepo;
            _hiddenRepo = hiddenRepo;
            _friendRequestRepo = friendRequestRepo;
        }
        public async Task<List<ChatConversationItem>> GetConversations(int currentUserId)
        {
            var currentUser = await _userRepo.GetByIdAsync(currentUserId);
            var hiddenUserIds = await _hiddenRepo.GetHiddenUserIdsAsync(currentUserId);
            var hiddenGroupIds = await _hiddenRepo.GetHiddenGroupIdsAsync(currentUserId);
            var friendIds = await _friendRequestRepo.GetAcceptedFriendIdsAsync(currentUserId);

            var users = (await _userRepo.GetByFriendIdsAsync(friendIds))
                .Where(u => !hiddenUserIds.Contains(u.Id))
                .ToList();

            // Add all Admin users
            var adminUsers = (await _userRepo.GetAllUsersAsync())
                .Where(u => u.Role == "Admin")
                .Where(u => u.Id != currentUserId)
                .Where(u => !hiddenUserIds.Contains(u.Id))
                .ToList();

            // Avoid duplicates
            users = users
                .Concat(adminUsers)
                .GroupBy(u => u.Id)
                .Select(g => g.First())
                .ToList();
            var conversations = new List<ChatConversationItem>();
            var unreadCounts = await _messageRepo.GetUnreadCountsAsync(currentUserId);
            foreach (var user in users)
            {
                var lastMessage = await _messageRepo.GetLastMessageAsync(currentUserId, user.Id, currentUserId);
                string? lastMessageText = null;
                if (lastMessage != null)
                    lastMessageText = lastMessage.DeletedStatus == "EveryOne" ? "This message was deleted" : lastMessage.Text;
                conversations.Add(new ChatConversationItem
                {
                    IsGroup = false,
                    Id = user.Id,
                    DisplayName = user.username,
                    ProfileImagePath = user.ProfileImagePath,
                    LastMessageText = lastMessageText,
                    LastMessageTime = lastMessage?.SentAt,
                    FileType = lastMessage?.FileType,
                    FileName = lastMessage?.FileName,
                    UnreadCount = unreadCounts.GetValueOrDefault(user.Id, 0),
                    IsOnline = user.IsOnline,
                    IsAdmin = user.Role == "Admin"
                });
            }
            var userGroups = (await _groupRepo.GetUserGroupsAsync(currentUserId))
                .Where(g => !hiddenGroupIds.Contains(g.GroupId))
                .ToList();
            var userGroupIds = userGroups.Select(g => g.GroupId).ToList();
            Dictionary<int, int> groupUnreadCounts = new();
            if (userGroupIds.Any())
                groupUnreadCounts = await _groupMsgRecipientRepo.GetGroupUnreadCountsAsync(currentUserId, userGroupIds);
            foreach (var g in userGroups)
            {
                var lastMsg = await _groupMsgRepo.GetLastMessageAsync(g.GroupId, currentUserId);
                string? text = null;
                if (lastMsg != null)
                {
                    if (lastMsg.DeletedStatus == "EveryOne")
                        text = "This message was deleted";
                    else if (lastMsg.FileType != null)
                        text = $"{lastMsg.SenderName} shared a {lastMsg.FileType}";
                    else
                        text = lastMsg.Text;
                }
                conversations.Add(new ChatConversationItem
                {
                    IsGroup = true,
                    Id = g.GroupId,
                    DisplayName = g.GroupName,
                    ProfileImagePath = g.ProfileImagePath,
                    LastMessageText = text ?? $"{g.UserIds.Count} members",
                    LastMessageTime = lastMsg?.SentAt,
                    UnreadCount = groupUnreadCounts.GetValueOrDefault(g.GroupId, 0),
                    MemberCount = g.UserIds.Count
                });
            }
            return conversations
                .OrderByDescending(c => c.LastMessageTime ?? DateTime.MinValue)
                .ToList();
        }
        public async Task<List<ChatConversationItem>> GetHiddenConversations(int currentUserId)
        {
            var hiddenUserIds = await _hiddenRepo.GetHiddenUserIdsAsync(currentUserId);
            var hiddenGroupIds = await _hiddenRepo.GetHiddenGroupIdsAsync(currentUserId);
            var users = await _userRepo.Query()
                .Where(u => hiddenUserIds.Contains(u.Id))
                .ToListAsync();
            var conversations = new List<ChatConversationItem>();
            var unreadCounts = await _messageRepo.GetUnreadCountsAsync(currentUserId);
            foreach (var user in users)
            {
                var lastMessage = await _messageRepo.GetLastMessageAsync(currentUserId, user.Id, currentUserId);
                string? lastMessageText = lastMessage?.DeletedStatus == "EveryOne"
                    ? "This message was deleted"
                    : lastMessage?.Text;
                conversations.Add(new ChatConversationItem
                {
                    IsGroup = false,
                    Id = user.Id,
                    DisplayName = user.username,
                    ProfileImagePath = user.ProfileImagePath,
                    LastMessageText = lastMessageText,
                    LastMessageTime = lastMessage?.SentAt,
                    FileType = lastMessage?.FileType,
                    FileName = lastMessage?.FileName,
                    UnreadCount = unreadCounts.GetValueOrDefault(user.Id, 0),
                    IsOnline = user.IsOnline
                });
            }
            var userGroups = await _groupRepo.Query()
                .Where(g => hiddenGroupIds.Contains(g.GroupId))
                .ToListAsync();
            var userGroupIds = userGroups.Select(g => g.GroupId).ToList();
            Dictionary<int, int> groupUnreadCounts = new();
            if (userGroupIds.Any())
                groupUnreadCounts = await _groupMsgRecipientRepo.GetGroupUnreadCountsAsync(currentUserId, userGroupIds);
            foreach (var g in userGroups)
            {
                var lastMsg = await _groupMsgRepo.GetLastMessageAsync(g.GroupId, currentUserId);
                string? text = null;
                if (lastMsg != null)
                {
                    if (lastMsg.DeletedStatus == "EveryOne")
                        text = "This message was deleted";
                    else if (lastMsg.FileType != null)
                        text = $"{lastMsg.SenderName} shared a {lastMsg.FileType}";
                    else
                        text = lastMsg.Text;
                }
                conversations.Add(new ChatConversationItem
                {
                    IsGroup = true,
                    Id = g.GroupId,
                    DisplayName = g.GroupName,
                    ProfileImagePath = g.ProfileImagePath,
                    LastMessageText = text ?? $"{g.UserIds.Count} members",
                    LastMessageTime = lastMsg?.SentAt,
                    UnreadCount = groupUnreadCounts.GetValueOrDefault(g.GroupId, 0),
                    MemberCount = g.UserIds.Count
                });
            }
            return conversations
                .OrderByDescending(c => c.LastMessageTime ?? DateTime.MinValue)
                .ToList();
        }
        public async Task<(List<UsersList> AllUsers, List<GroupCredentials> AllGroups)> GetHiddenViewExtras(
            int currentUserId, List<int> hiddenUserIds, List<int> hiddenGroupIds)
        {
            var friendIds = await _friendRequestRepo.GetAcceptedFriendIdsAsync(currentUserId);
            var allUsers = await _userRepo.Query()
                .Where(u => u.Id != currentUserId && friendIds.Contains(u.Id) && !hiddenUserIds.Contains(u.Id))
                .ToListAsync();
            var allGroups = (await _groupRepo.Query().ToListAsync())
                .Where(g => g.UserIds.Contains(currentUserId) && !hiddenGroupIds.Contains(g.GroupId))
                .ToList();
            return (allUsers, allGroups);
        }
        public async Task<List<int>> GetHiddenUserIds(int currentUserId)
            => await _hiddenRepo.GetHiddenUserIdsAsync(currentUserId);
        public async Task<List<int>> GetHiddenGroupIds(int currentUserId)
            => await _hiddenRepo.GetHiddenGroupIdsAsync(currentUserId);
        public async Task<bool> VerifyHiddenAccess(int userId, string password, string? confirmPassword)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return false;
            if (string.IsNullOrEmpty(user.HiddenPassword))
            {
                if (password != confirmPassword) return false;
                user.HiddenPassword = password;
                await _userRepo.SaveChangesAsync();
                return true;
            }
            else
            {
                return user.HiddenPassword == password;
            }
        }
        public bool IsHiddenAccessGranted(int userId, int sessionUserId)
            => userId == sessionUserId;
    }
}