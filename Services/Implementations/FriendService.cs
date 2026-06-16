using Chat_App.Models;
using Chat_App.Repositories;
using Chat_App.Services.Dtos;
using Chat_App.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Crypto.Prng;
namespace Chat_App.Services.Implementations
{
    public class FriendService : IFriendService
    {
        private readonly IUserRepository _userRepo;
        private readonly IFriendRequestRepository _friendRequestRepo;
        private readonly INotificationService _notification;
        public FriendService(IUserRepository userRepo, IFriendRequestRepository friendRequestRepo, INotificationService notification)
        {
            _userRepo = userRepo;
            _friendRequestRepo = friendRequestRepo;
            _notification = notification;
        }
        public async Task<FriendRequestResult> SendFriendRequest(int currentUserId, int receiverId)
        {
            if (currentUserId == receiverId)
                return new FriendRequestResult { Success = false };
            var existingFriendship = await _friendRequestRepo.GetFriendshipAsync(currentUserId, receiverId);
            if (existingFriendship != null)
            {
                if (existingFriendship.Status == "Accepted" || existingFriendship.Status == "Pending")
                    return new FriendRequestResult { Success = false };
            }
            var request = new FriendRequest
            {
                SenderId = currentUserId,
                ReceiverId = receiverId,
                Status = "Pending",
                SentAt = DateTime.UtcNow,
                Bond = "Unknown"
            };
            await _friendRequestRepo.AddAsync(request);
            await _notification.NotifyFriendRequestReceived(receiverId, currentUserId);
            return new FriendRequestResult { Success = true };
        }
        public async Task AcceptFriendRequest(int currentUserId, int requestId)
        {
            var request = await _friendRequestRepo.GetByIdAndReceiverAsync(requestId, currentUserId);
            if (request == null) return;
            request.Status = "Accepted";
            request.RespondedAt = DateTime.UtcNow;
            await _friendRequestRepo.SaveChangesAsync();
            await _notification.NotifyFriendRequestAccepted(request.SenderId, currentUserId);
        }
        public async Task RejectFriendRequest(int currentUserId, int requestId)
        {
            var request = await _friendRequestRepo.GetByIdAndReceiverAsync(requestId, currentUserId);
            if (request == null) return;
            await _friendRequestRepo.DeleteAsync(request);
        }
        public async Task<List<PendingFriendRequestDto>> GetPendingRequests(int currentUserId)
        {
            var requests = await _friendRequestRepo.GetPendingForReceiverAsync(currentUserId);
            var result = new List<PendingFriendRequestDto>();
            foreach (var f in requests)
            {
                var sender = await _userRepo.GetByIdAsync(f.SenderId);
                result.Add(new PendingFriendRequestDto
                {
                    Id = f.Id,
                    SenderId = f.SenderId,
                    SenderName = sender?.username ?? "",
                    SenderProfileImage = sender?.ProfileImagePath,
                    SentAt = f.SentAt
                });
            }
            return result;
        }
        public async Task<List<SentFriendRequestDto>> GetSentRequests(int currentUserId)
        {
            var requests = await _friendRequestRepo.GetPendingBySenderAsync(currentUserId);
            var result = new List<SentFriendRequestDto>();
            foreach (var f in requests)
            {
                var receiver = await _userRepo.GetByIdAsync(f.ReceiverId);
                result.Add(new SentFriendRequestDto
                {
                    Id = f.Id,
                    ReceiverId = f.ReceiverId,
                    ReceiverName = receiver?.username ?? "",
                    ReceiverProfileImage = receiver?.ProfileImagePath,
                    SentAt = f.SentAt
                });
            }
            return result;
        }
        public async Task<FriendshipStatusDto> GetFriendshipStatus(int currentUserId, int userId)
        {
            var friendship = await _friendRequestRepo.GetFriendshipAsync(currentUserId, userId);
            if (friendship == null)
                return new FriendshipStatusDto { Status = "None" };
            return new FriendshipStatusDto
            {
                Status = friendship.Status,
                RequestId = friendship.Id,
                IsSender = friendship.SenderId == currentUserId
            };
        }
        public async Task<UserSearchResultDto> GetAllUsersForFriendRequest(int currentUserId, int skip = 0, int take = 15)
        {
            var (users, totalCount) = await _userRepo.SearchExceptAsync(currentUserId, skip, take);
            // Remove Admin users
            users = users.Where(u => u.Role != "Admin").ToList();

            var userIds = users.Select(u => u.Id).ToList();
            var friendships = await _friendRequestRepo.GetFriendshipsForUsersAsync(currentUserId, userIds);
            var result = users.Select(u =>
            {
                var friendship = friendships.FirstOrDefault(f =>
                    (f.SenderId == currentUserId && f.ReceiverId == u.Id) ||
                    (f.SenderId == u.Id && f.ReceiverId == currentUserId));
              
                FriendshipStatusDto status;
                if (friendship == null)
                    status = new FriendshipStatusDto { Status = "None" };
                else
                    status = new FriendshipStatusDto
                    {
                        Status = friendship.Status,
                        RequestId = friendship.Id,
                        IsSender = friendship.SenderId == currentUserId
                    };
                return new UserForFriendRequestDto
                {
                    Id = u.Id,
                    Username = u.username,
                    ProfileImagePath = u.ProfileImagePath,
                    IsOnline = u.IsOnline,
                    FriendshipStatus = status
                };
            }).ToList();
            return new UserSearchResultDto { Users = result, TotalCount = totalCount };
        }
        public async Task<List<FriendDto>> GetFriends(int currentUserId)
        {
            var friendIds = await _friendRequestRepo.GetAcceptedFriendIdsAsync(currentUserId);
            return await _userRepo.Query()
                .Where(u => friendIds.Contains(u.Id))
                .Select(u => new FriendDto
                {
                    Id = u.Id,
                    Username = u.username,
                    ProfileImagePath = u.ProfileImagePath,
                    IsOnline = u.IsOnline,
                    LastSeen = u.LastSeen
                })
                .ToListAsync();
        }
        public async Task UpdateRelationship(int currentUserId, int userId, string relationship)
        {
            var friend = await _friendRequestRepo.GetFriendshipAsync(currentUserId, userId);
            if (friend != null)
            {
                friend.Bond = relationship;
                await _friendRequestRepo.SaveChangesAsync();
            }
        }
        public async Task CancelFriendRequest(int currentUserId, int requestId)
        {
            var request = await _friendRequestRepo.GetByIdAndSenderAsync(requestId, currentUserId);
            if (request == null) return;
            await _friendRequestRepo.DeleteAsync(request);
        }
    }
}