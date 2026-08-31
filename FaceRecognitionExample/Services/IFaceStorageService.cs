using FaceRecognitionExample.Models;

namespace FaceRecognitionExample.Services
{
    public interface IFaceStorageService
    {
        Task InitializeAsync();
        Task SaveFaceRecordAsync(FaceRecord record);
        Task<List<FaceRecord>> GetAllFaceRecordsAsync();
        Task<List<FaceRecord>> GetRecentFaceRecordsAsync(DateTime sinceUtc);
    }
}
