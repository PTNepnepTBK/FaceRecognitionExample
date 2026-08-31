using FaceRecognitionExample.Models;
using SQLite;

namespace FaceRecognitionExample.Services
{
    public class FaceStorageService : IFaceStorageService
    {
        private SQLiteAsyncConnection _connection;
        private readonly List<FaceRecord> _cache = new();
        private readonly object _cacheLock = new();
        private bool _isInitialized = false;
        
        public FaceStorageService()
        {
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "FaceRecog.db3");
            _connection = new SQLiteAsyncConnection(dbPath);
            await _connection.CreateTableAsync<FaceRecord>();

            // Warm-up: load recent records into cache
            var cutoff = DateTime.UtcNow.AddMinutes(-AppConstants.CooldownMinutes);
            var existing = await _connection.Table<FaceRecord>()
                                            .Where(r => r.DetectedAtUtc >= cutoff)
                                            .ToListAsync();
            lock (_cacheLock)
            {
                _cache.AddRange(existing);
            }
            _isInitialized = true;
        }

        public async Task SaveFaceRecordAsync(FaceRecord record)
        {
            await InitializeAsync();

            // Add to in-memory cache + prune expired entries
            lock (_cacheLock)
            {
                _cache.Add(record);
                var cutoff = DateTime.UtcNow.AddMinutes(-AppConstants.CooldownMinutes);
                _cache.RemoveAll(r => r.DetectedAtUtc < cutoff);
            }

            // Persist to SQLite (tetap await, ikut terhitung di stopwatch)
            await _connection.InsertAsync(record);
        }

        public async Task<List<FaceRecord>> GetAllFaceRecordsAsync()
        {
            await InitializeAsync();
            return await _connection.Table<FaceRecord>().ToListAsync();
        }

        public async Task<List<FaceRecord>> GetRecentFaceRecordsAsync(DateTime sinceUtc)
        {
            await InitializeAsync();

            // Pure in-memory filter — sub-millisecond
            lock (_cacheLock)
            {
                return _cache.Where(r => r.DetectedAtUtc >= sinceUtc).ToList();
            }
        }
    }
}
