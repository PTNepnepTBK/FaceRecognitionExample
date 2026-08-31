using FaceRecognitionExample.Models;

namespace FaceRecognitionExample.Services
{
    public class FaceMatchingService : IFaceMatchingService
    {
        public bool IsFaceDuplicate(float[] newEmbedding, List<FaceRecord> recentRecords, float threshold)
        {
            foreach (var record in recentRecords)
            {
                if (record.EmbeddingBlob == null || record.EmbeddingBlob.Length == 0)
                    continue;

                float[] storedEmbedding = ConvertBlobToFloatArray(record.EmbeddingBlob);
                
                float similarity = CalculateCosineSimilarity(newEmbedding, storedEmbedding);
                
                if (similarity >= threshold)
                {
                    return true;
                }
            }

            return false;
        }

        public float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA.Length != vectorB.Length)
                return 0f;

            float dotProduct = 0f;
            float normA = 0f;
            float normB = 0f;

            for (int i = 0; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
                normA += vectorA[i] * vectorA[i];
                normB += vectorB[i] * vectorB[i];
            }

            if (normA == 0f || normB == 0f)
                return 0f;

            return dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        private float[] ConvertBlobToFloatArray(byte[] blob)
        {
            float[] result = new float[blob.Length / sizeof(float)];
            Buffer.BlockCopy(blob, 0, result, 0, blob.Length);
            return result;
        }
    }
}
