using Chat_App.Models;
using Chat_App.Repositories;
using Chat_App.Services.Dtos;
using Chat_App.Services.Implementations;
using Chat_App.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
namespace Chat_App.Controllers
{
    [Authorize]
    public class AppController : Controller
    {
        private readonly IChatIndexService _chatIndex;
        private readonly IMessageService _message;
        private readonly IFriendService _friend;
        private readonly IGroupService _group;
        private readonly IFileUploadService _fileUpload;
        private readonly IProfileService _profile;
        private readonly IBlockService _block;
        private readonly IChatSettingsService _chatSettings;
        private readonly IPushService _push;
        private readonly IMomentService momentService;
        private readonly IMomentRepository _momentRepo;
        private readonly IUserRepository _userRepo;
        private readonly ApplicationDBContext dbContext;
        public AppController(
            IChatIndexService chatIndex,
            IMessageService message,
            IFriendService friend,
            IGroupService group,
            IFileUploadService fileUpload,
            IProfileService profile,
            IBlockService block,
            IChatSettingsService chatSettings,
            IPushService push,
            IMomentService momentService,
            IMomentRepository momentRepo,
            IUserRepository userRepo,
            ApplicationDBContext context)
        {
            _chatIndex = chatIndex;
            _message = message;
            _friend = friend;
            _group = group;
            _fileUpload = fileUpload;
            _profile = profile;
            _block = block;
            _chatSettings = chatSettings;
            _push = push;
            this.momentService = momentService;
            _momentRepo = momentRepo;
            _userRepo = userRepo;
            dbContext = context;
        }
        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        [HttpPost]
        public IActionResult UploadProfile() => Ok();
        public async Task<IActionResult> Moments()
        {
            var userId = CurrentUserId;
            var moments = await momentService.GetActiveMoments(userId);
            var userIds = moments.Select(u => u.Id).ToList();
            var userlist = await _userRepo.GetByIdsDictionaryAsync(userIds);
            var currentUsername = User.FindFirstValue(ClaimTypes.Name);
            ViewBag.CurrentUserId = userId;
            ViewBag.CurrentUsername = currentUsername;
            ViewBag.Users = userlist;
            return RedirectToAction("Index");
        }
        [HttpPost]
        public async Task<IActionResult> UploadMoment()
        {
            var file = Request.Form.Files.FirstOrDefault();
            var fileType = Request.Form["fileType"].FirstOrDefault() ?? "image";
            var caption = Request.Form["caption"].FirstOrDefault();
            if (file == null) return BadRequest("No file uploaded");
            try
            {
                var moment = await momentService.UploadMoment(CurrentUserId, file, fileType, caption);
                return Json(new { success = true, momentId = moment.Id, mediaUrl = moment.MediaUrl });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
        }
        [HttpPost]
        public async Task<IActionResult> MarkMomentViewed(int momentId)
        {
            await momentService.MarkAsViewed(momentId, CurrentUserId);
            return Ok();
        }
        [HttpGet]
        public async Task<IActionResult> GetMomentViews(int momentId)
        {
            var views = await _momentRepo.GetMomentViewsAsync(momentId);
            return Json(views);
        }
        public async Task<IActionResult> Index()
        {
            HttpContext.Session.Remove("HiddenAccessGranted");
            var userId = CurrentUserId;
            var user = await _profile.GetProfile(userId);
            ViewBag.CurrentUserProfileImage = user?.ProfileImagePath;
            ViewBag.CurrentUserUsername = user?.username;
            ViewBag.CurrentUserId = userId;
            ViewBag.CurrentUsername = user?.username;

            // ✅ Admin check
            bool isAdmin = User.IsInRole("Admin"); // or however you store the role
            ViewBag.IsAdmin = isAdmin;
            if (isAdmin)
            {
                var allUsers = await _userRepo.GetAllUsersWithLastMessageAsync(userId); // pass userId
                return View(allUsers);
            }
            var conversations = await _chatIndex.GetConversations(userId);
            var moments = await momentService.GetActiveMoments(userId);
            var userIds = moments.Select(m => m.UserId).ToList();
            var usersDict = await _userRepo.GetByIdsDictionaryAsync(userIds);
            ViewBag.Moments = moments;
            ViewBag.Users = usersDict;

            return View(conversations);
        }
        public IActionResult HiddenAccess()
        {
            var userId = CurrentUserId;
            var user = _profile.GetProfile(userId).Result;
            ViewBag.HasPassword = !string.IsNullOrEmpty(user?.HiddenPassword);
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> HiddenAccess(string password, string? confirmPassword)
        {
            var userId = CurrentUserId;
            var user = await _profile.GetProfile(userId);
            if (string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Password is required";
                ViewBag.HasPassword = !string.IsNullOrEmpty(user?.HiddenPassword);
                return View();
            }
            var verified = await _chatIndex.VerifyHiddenAccess(userId, password, confirmPassword);
            if (!verified && !string.IsNullOrEmpty(user?.HiddenPassword))
            {
                ViewBag.Error = "Incorrect password";
                ViewBag.HasPassword = true;
                return View();
            }
            if (!verified)
            {
                ViewBag.Error = "Passwords do not match";
                ViewBag.HasPassword = false;
                return View();
            }
            HttpContext.Session.SetInt32("HiddenAccessGranted", userId);
            return RedirectToAction("HiddenIndex");
        }
        public async Task<IActionResult> HiddenIndex()
        {
            var userId = CurrentUserId;
            var sessionId = HttpContext.Session.GetInt32("HiddenAccessGranted");
            if (userId != sessionId)
                return RedirectToAction("HiddenAccess", "App");
            var user = await _profile.GetProfile(userId);
            ViewBag.CurrentUserProfileImage = user?.ProfileImagePath;
            ViewBag.CurrentUserUsername = user?.username;
            var conversations = await _chatIndex.GetHiddenConversations(userId);
            var hiddenUserIds = await _chatIndex.GetConversations(userId);
            var viewExtras = await _chatIndex.GetHiddenViewExtras(userId,
                await GetHiddenUserIds(userId),
                await GetHiddenGroupIds(userId));
            ViewBag.AllUsers = viewExtras.AllUsers;
            ViewBag.AllGroups = viewExtras.AllGroups;
            return View(conversations);
        }
        private async Task<List<int>> GetHiddenUserIds(int userId) => await Task.Run(() => new List<int>());
        private async Task<List<int>> GetHiddenGroupIds(int userId) => await Task.Run(() => new List<int>());
        public IActionResult Chat(int id)
        {
            var userId = CurrentUserId;
            bool isAdmin = User.IsInRole("Admin");

            var (isFriend, requestStatus, isSender, receiver,
                currentUser, isBlocked, relationship)
                = _group.GetChatData(id, userId);

            ViewBag.IsAdmin = isAdmin;
            ViewBag.IsFriend = isFriend;

            // Check if the receiver is an admin
            bool isReceiverAdmin = receiver != null && receiver.Role == "Admin";
            ViewBag.IsReceiverAdmin = isReceiverAdmin;
           

            if (!isFriend && !isAdmin)
            {
                ViewBag.RequestStatus = requestStatus ?? "None";
                ViewBag.IsSender = isSender;
            }

            if (receiver != null)
            {
                ViewBag.ReceiverName = receiver.username;
                ViewBag.ReceiverProfileImage = receiver.ProfileImagePath;
                ViewBag.IsOnline = receiver.IsOnline;
                ViewBag.LastSeen = receiver.LastSeen;
            }

            ViewBag.IsBlocked = isBlocked;
            ViewBag.Id = id;
            ViewBag.RelationShip = relationship;

            return View();
        }
        public IActionResult GroupChat(int id)
        {
            var userId = CurrentUserId;
            var (group, members, currentUser) = _group.GetGroupChatData(id, userId);
            if (group == null) return NotFound();
            if (group.UserIds == null || !group.UserIds.Contains(userId))
                return Forbid();
            ViewBag.GroupName = group.GroupName;
            ViewBag.GroupId = group.GroupId;
            ViewBag.ProfileImagePath = group.ProfileImagePath;
            ViewBag.Members = members;
            ViewBag.CurrentUserId = userId;
            ViewBag.CurrentUserName = currentUser?.username;
            ViewBag.CurrentUserProfileImage = currentUser?.ProfileImagePath;
            return View();
        }
        public IActionResult Friends() => View();
        [HttpGet]
        public async Task<IActionResult> GetMessages(int receiverId, DateTime? before = null, int? beforeId = null, int take = 50)
        {
            var result = await _message.GetMessages(CurrentUserId, receiverId, before, beforeId, take);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> GetGroupMessages(int groupId, DateTime? before = null, int? beforeId = null, int take = 50)
        {
            var result = await _message.GetGroupMessages(CurrentUserId, groupId, before, beforeId, take);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> GetGroupMessageStatus(int messageId)
        {
            var result = await _message.GetGroupMessageStatus(CurrentUserId, messageId);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> StaredMessages()
        {
            var messages = await _message.GetStarredMessages();
            return View(messages);
        }
        [HttpPost]
        public async Task<IActionResult> MessageStared(int messageId)
        {
            await _message.StarMessage(messageId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> MessageUnstarred(int messageId)
        {
            await _message.UnstarMessage(messageId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> GroupMessageStared(int messageId)
        {
            await _message.StarGroupMessage(messageId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> GroupMessageUnstarred(int messageId)
        {
            await _message.UnstarGroupMessage(messageId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> RemoveStar(int messageId, string messageType)
        {
            await _message.RemoveStar(messageId, messageType);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> DeleteForEveryOne(int messageid)
        {
            await _message.DeleteForEveryone(messageid, CurrentUserId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> DeleteForMe(int messageid)
        {
            var result = await _message.DeleteForMe(messageid, CurrentUserId);
            if (result == null) return NotFound();
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> UndoDelete(int messageid)
        {
            var result = await _message.UndoDelete(messageid, CurrentUserId);
            if (result == null) return NotFound();
            return Json(result);
        }
        [HttpPost]
        public async Task<IActionResult> DeleteGroupMessageForEveryOne(int messageid)
        {
            await _message.DeleteGroupMessageForEveryone(messageid, CurrentUserId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> DeleteGroupMessageForMe(int messageid)
        {
            await _message.DeleteGroupMessageForMe(messageid, CurrentUserId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> UndoGroupDelete(int messageid)
        {
            var result = await _message.UndoGroupDelete(messageid, CurrentUserId);
            if (result == null) return NotFound();
            return Json(result);
        }
        [HttpPost]
        public async Task<IActionResult> BlockUser(int userId)
        {
            await _block.BlockUser(CurrentUserId, userId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> UnblockUser(int userId)
        {
            await _block.UnblockUser(CurrentUserId, userId);
            return Ok();
        }
        [HttpGet]
        public async Task<IActionResult> CheckUsername(string username)
        {
            var result = await _block.CheckUsername(username);
            return Json(result);
        }
        [HttpPost]
        public async Task<IActionResult> ToggleHideUser(int hiddenUserId)
        {
            await _group.ToggleHideUser(CurrentUserId, hiddenUserId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> ToggleHideGroup(int groupId)
        {
            await _group.ToggleHideGroup(CurrentUserId, groupId);
            return Ok();
        }
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var profile = await _profile.GetProfile(CurrentUserId);
            return View(profile);
        }
        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string username, string name, string nickName)
        {
            await _profile.UpdateProfile(CurrentUserId, username, name, nickName);
            TempData["ProfileSaved"] = true;
            return RedirectToAction("Profile");
        }
        [HttpPost]
        public async Task<IActionResult> UploadFile(int senderId, int receiverId, string fileType)
        {
            try
            {
                var file = Request.Form.Files.FirstOrDefault();
                if (file == null) return BadRequest("No file uploaded");
                var result = await _fileUpload.UploadFile(file, fileType);
                return Json(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }
        [HttpPost]
        public async Task<IActionResult> UploadGroupFile(int senderId, int groupId, string fileType)
        {
            try
            {
                var file = Request.Form.Files.FirstOrDefault();
                if (file == null) return BadRequest("No file uploaded");
                var result = await _fileUpload.UploadGroupFile(file, fileType);
                return Json(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }
        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] GroupCredentials group)
        {
            if (group == null) return BadRequest();
            var result = await _group.CreateGroup(group, CurrentUserId);
            return Ok(result);
        }
        [HttpPost]
        public async Task<IActionResult> SetTimeColor(int peerId, string color)
        {
            await _chatSettings.SetTimeColor(CurrentUserId, peerId, color);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> SetBackground(int peerId, string? backgroundFit)
        {
            try
            {
                var file = Request.Form.Files.FirstOrDefault();
                if (file == null || file.Length == 0)
                    return BadRequest(new { message = "No file uploaded" });
                var result = await _chatSettings.SetBackground(CurrentUserId, peerId, file, backgroundFit);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetBackground(int peerId)
        {
            var result = await _chatSettings.GetBackground(CurrentUserId, peerId);
            return Json(result);
        }
        [HttpPost]
        public async Task<IActionResult> RemoveBackground(int peerId)
        {
            await _chatSettings.RemoveBackground(CurrentUserId, peerId);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> UpdateBackgroundFit(int peerId, string fit)
        {
            await _chatSettings.UpdateBackgroundFit(CurrentUserId, peerId, fit);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> SendFriendRequest(int receiverId)
        {
            var result = await _friend.SendFriendRequest(CurrentUserId, receiverId);
            if (!result.Success) return BadRequest("Cannot send request");
            return Ok(result);
        }
        [HttpPost]
        public async Task<IActionResult> AcceptFriendRequest(int requestId)
        {
            await _friend.AcceptFriendRequest(CurrentUserId, requestId);
            return Ok(new { success = true });
        }
        [HttpPost]
        public async Task<IActionResult> RejectFriendRequest(int requestId)
        {
            await _friend.RejectFriendRequest(CurrentUserId, requestId);
            return Ok(new { success = true });
        }
        [HttpPost]
        public async Task<IActionResult> CancelFriendRequest(int requestId)
        {
            await _friend.CancelFriendRequest(CurrentUserId, requestId);
            return Ok(new { success = true });
        }
        [HttpGet]
        public async Task<IActionResult> GetPendingFriendRequests()
        {
            var result = await _friend.GetPendingRequests(CurrentUserId);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> GetSentFriendRequests()
        {
            var result = await _friend.GetSentRequests(CurrentUserId);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> GetFriendshipStatus(int userId)
        {
            var result = await _friend.GetFriendshipStatus(CurrentUserId, userId);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> GetAllUsersForFriendRequest(int skip = 0, int take = 15)
        {
            var result = await _friend.GetAllUsersForFriendRequest(CurrentUserId, skip, take);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> GetFriends()
        {
            var result = await _friend.GetFriends(CurrentUserId);
            return Json(result);
        }
        [HttpPost]
        public async Task<IActionResult> UpdateRelationShip(int userid, string relationship)
        {
            await _friend.UpdateRelationship(CurrentUserId, userid, relationship);
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> SavePushSubscription([FromBody] SavePushSubscriptionDto dto)
        {
            await _push.SaveSubscription(CurrentUserId, dto);
            return Ok();
        }
        [HttpGet]
        public async Task<IActionResult> TestPush()
        {
            await _push.SendTestNotification(CurrentUserId);
            return Content("Push sent. Check console/server logs for errors.");
        }
        [HttpPost]
        public async Task<IActionResult> DeleteMoment(int momentId)
        {
            var ok = await momentService.DeleteMoment(momentId, CurrentUserId);
            return ok ? Json(new { success = true }) : BadRequest(new { success = false, error = "Not found or unauthorized" });
        }

        [HttpGet]
        public IActionResult Forward()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetForwardRecipients()
        {
            try
            {
                // Get friends (connected users)
                var friends = await _friend.GetFriends(CurrentUserId);
                var users = friends.Select(f => new ForwardRecipientDto
                {
                    Id = f.Id,
                    Username = f.Username,
                    ProfileImagePath = f.ProfileImagePath,
                    IsOnline = f.IsOnline,
                    LastSeen = f.LastSeen
                }).ToList();

                // Get user's groups
                using var scope = HttpContext.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();

                var userGroups = dbContext.group
                    .Where(g => g.UserIds.Contains(CurrentUserId))
                    .ToList();

                var groups = userGroups.Select(g => new ForwardGroupDto
                {
                    GroupId = g.GroupId,
                    GroupName = g.GroupName,
                    ProfileImagePath = g.ProfileImagePath,
                    UserIds = g.UserIds
                }).ToList();

                return Json(new { users, groups });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message, users = new List<ForwardRecipientDto>(), groups = new List<ForwardGroupDto>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ForwardOld()
        {
            // Get forward message data from session
            var forwardDataJson = HttpContext.Session.GetString("ForwardMessage");
            if (string.IsNullOrEmpty(forwardDataJson))
            {
                return RedirectToAction("Index");
            }

            var message = System.Text.Json.JsonSerializer.Deserialize<ForwardMessageDto>(forwardDataJson)
                ?? new ForwardMessageDto();

            // Get friends (connected users)
            var friends = await _friend.GetFriends(CurrentUserId);
            var users = friends.Select(f => new ForwardRecipientDto
            {
                Id = f.Id,
                Username = f.Username,
                ProfileImagePath = f.ProfileImagePath,
                IsOnline = f.IsOnline,
                LastSeen = f.LastSeen
            }).ToList();

            // Get user's groups
            var groups = new List<ForwardGroupDto>();
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();

            var userGroups = dbContext.group
                .Where(g => g.UserIds.Contains(CurrentUserId))
                .ToList();

            groups = userGroups.Select(g => new ForwardGroupDto
            {
                GroupId = g.GroupId,
                GroupName = g.GroupName,
                ProfileImagePath = g.ProfileImagePath,
                UserIds = g.UserIds
            }).ToList();

            var model = new ForwardViewModel
            {
                Message = message,
                Users = users,
                Groups = groups
            };

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> ForwardMessage([FromBody] ForwardRequest request)
        {
            try
            {
                var currentUser = await _profile.GetProfile(CurrentUserId);
                var currentUsername = currentUser?.Name ?? "Someone";
                var currentUserProfileImage = currentUser?.ProfileImagePath;

                foreach (var recipient in request.Recipients)
                {
                    if (recipient.Type == "user")
                    {
                        // Forward to user
                        var msg = new Message
                        {
                            SenderId = CurrentUserId,
                            ReceiverId = recipient.Id,
                            Text = request.Text,
                            FileType = request.FileType,
                            FileName = request.FileName,
                            SentAt = DateTime.UtcNow,
                            IsDelivered = false,
                            IsRead = false,
                            DeletedStatus = "None",
                            IsForwarded = true,
                            OriginalSenderId = request.MessageId > 0 ? CurrentUserId : null
                        };

                        dbContext.message.Add(msg);
                        await dbContext.SaveChangesAsync();

                        // Send real-time notification via SignalR
                        var connIds = ConnectionManager.GetConnections(recipient.Id);
                        var hubContext = HttpContext.RequestServices.GetRequiredService<IHubContext<ChatHub>>();

                        foreach (var connId in connIds)
                        {
                            await hubContext.Clients.Client(connId).SendAsync(
                                "ReceiveMessage",
                                CurrentUserId,
                                request.Text,
                                msg.MessageId
                            );
                        }
                    }
                    else if (recipient.Type == "group")
                    {
                        // Forward to group
                        var groupMsg = new GroupMessage
                        {
                            GroupId = recipient.Id,
                            SenderId = CurrentUserId,
                            SenderName = $"{currentUsername} (forwarded)",
                            SenderProfileImage = currentUserProfileImage,
                            Text = request.Text,
                            FileType = request.FileType,
                            FileName = request.FileName,
                            SentAt = DateTime.UtcNow,
                            DeletedStatus = "None",
                            IsForwarded = true,
                            OriginalMessageId = request.MessageId > 0 ? request.MessageId : null
                        };

                        dbContext.groupMessage.Add(groupMsg);
                        await dbContext.SaveChangesAsync();

                        // Get group members and send notifications
                        var group = dbContext.group.FirstOrDefault(g => g.GroupId == recipient.Id);
                        if (group != null)
                        {
                            var hubContext = HttpContext.RequestServices.GetRequiredService<IHubContext<ChatHub>>();

                            foreach (var memberId in group.UserIds)
                            {
                                if (memberId == CurrentUserId) continue;

                                // Save delivery status
                                var recipientStatus = new GroupMessageRecipient
                                {
                                    GroupMessageId = groupMsg.GroupMessageId,
                                    UserId = memberId,
                                    IsDelivered = false,
                                    IsRead = false
                                };
                                dbContext.groupMessageRecipient.Add(recipientStatus);

                                // Send real-time notification
                                var connIds = ConnectionManager.GetConnections(memberId);
                                foreach (var connId in connIds)
                                {
                                    await hubContext.Clients.Client(connId).SendAsync(
                                        "ReceiveGroupMessage",
                                        CurrentUserId,
                                        $"{currentUsername} (forwarded)",
                                        currentUserProfileImage,
                                        recipient.Id,
                                        request.Text,
                                        groupMsg.GroupMessageId,
                                        groupMsg.SentAt.ToString("o")
                                    );
                                }
                            }

                            await dbContext.SaveChangesAsync();
                        }
                    }
                }

                // Clear the forward message from session
                HttpContext.Session.Remove("ForwardMessage");

                return Json(new ForwardResult { Success = true });
            }
            catch (Exception ex)
            {
                return Json(new ForwardResult { Success = false, Message = ex.Message });
            }
        }
    }
}