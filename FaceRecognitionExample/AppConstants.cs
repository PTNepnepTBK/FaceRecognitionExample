namespace FaceRecognitionExample
{
    public static class AppConstants
    {
        // Cosine similarity threshold for face matching (0.0 to 1.0)
        // Usually 0.6 is a good starting point for FaceONNX embeddings
        public const float SimilarityThreshold = 0.6f;

        // Cooldown duration in minutes to prevent duplicate face detections
        public const int CooldownMinutes = 5;
    }
}
