namespace FanX.Services
{
    public class AppSettingService
    {
        private readonly DatabaseService _databaseService;
        private readonly Dictionary<string, string?> _cache = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public AppSettingService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task<string?> GetSettingAsync(string key, string? defaultValue = null)
        {
            await _semaphore.WaitAsync();
            try
            {
                // Check cache first
                if (_cache.TryGetValue(key, out var cachedValue))
                {
                    return cachedValue;
                }

                // Load from database
                var settingExists = await _databaseService.Db.Queryable<Models.AppSetting>()
                    .Where(s => s.Key == key)
                    .AnyAsync();
                
                string? value = defaultValue;
                if (settingExists)
                {
                    var setting = await _databaseService.Db.Queryable<Models.AppSetting>()
                        .Where(s => s.Key == key)
                        .FirstAsync();
                    value = setting?.Value ?? defaultValue;
                }
                
                _cache[key] = value;
                return value;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<int> GetIntSettingAsync(string key, int defaultValue)
        {
            var value = await GetSettingAsync(key);
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        public async Task SetSettingAsync(string key, string? value)
        {
            await _semaphore.WaitAsync();
            try
            {
                var exists = await _databaseService.Db.Queryable<Models.AppSetting>()
                    .Where(s => s.Key == key)
                    .AnyAsync();

                if (exists)
                {
                    var existing = await _databaseService.Db.Queryable<Models.AppSetting>()
                        .Where(s => s.Key == key)
                        .FirstAsync();
                    existing.Value = value;
                    await _databaseService.Db.Updateable(existing).ExecuteCommandAsync();
                }
                else
                {
                    var newSetting = new Models.AppSetting
                    {
                        Key = key,
                        Value = value
                    };
                    await _databaseService.Db.Insertable(newSetting).ExecuteCommandAsync();
                }

                // Update cache
                _cache[key] = value;
                
                LoggerService.Info($"Setting '{key}' updated to '{value}'");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
        }
    }
}
