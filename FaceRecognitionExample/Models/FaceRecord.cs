using SQLite;

namespace FaceRecognitionExample.Models
{
    public class FaceRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Name { get; set; }

        public byte[] EmbeddingBlob { get; set; }

        public DateTime DetectedAtUtc { get; set; }
    }
}
