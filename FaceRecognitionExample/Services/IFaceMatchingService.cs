using FaceRecognitionExample.Models;

namespace FaceRecognitionExample.Services
{
    public interface IFaceMatchingService
    {
        bool IsFaceDuplicate(float[] newEmbedding, List<FaceRecord> recentRecords, float threshold);
        float CalculateCosineSimilarity(float[] vectorA, float[] vectorB);
    }
}
