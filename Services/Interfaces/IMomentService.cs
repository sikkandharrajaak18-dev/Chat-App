using Chat_App.Models;

namespace Chat_App.Services.Interfaces
{
    public interface IMomentService
    {
        Task<Moment> UploadMoment(int userId, IFormFile file, string fileType, string? caption);
        Task<List<Moment>> GetActiveMoments(int currentUserId);
        Task<List<Moment>> GetMyMoments(int userId);
        Task MarkAsViewed(int momentId, int viewerId);
       Task<int> GetViewCount(int momentId);
        Task DeleteExpiredMoments();
        Task<bool> DeleteMoment(int momentId, int userId);
    }
}
