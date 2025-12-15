using FanX.Models;

namespace FanX.Services
{
    public class MaintenanceService : IHostedService, IDisposable
    {
        private bool _initialized;
        private readonly IServiceProvider _serviceProvider;
        private Timer? _timer;
        private DateTime _lastLogCleanup = DateTime.MinValue;
        private DateTime _lastSensorCleanup = DateTime.MinValue;
        private const int DefaultSensorRetentionDays = 30; // Default: keep 30 days of data

        public MaintenanceService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Run every hour for maintenance tasks (no need to run every second)
            _timer = new Timer(async void (_) => await DoWork(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
            return Task.CompletedTask;
        }

        private async Task DoWork()
        {
            var nowUtc = DateTime.UtcNow;
            LoggerService.Debug($"MaintenanceService executing at {nowUtc} UTC.");
            if (!_initialized)
            {
                _initialized = true;
                _lastLogCleanup = nowUtc;
                _lastSensorCleanup = nowUtc;
                LoggerService.Debug("MaintenanceService initialization complete, skipping first cleanup.");
                return;
            }
            
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DatabaseService>().Db;
            var settings = await db.Queryable<AppSetting>().ToListAsync();

            // Clean old logs daily
            if (nowUtc - _lastLogCleanup >= TimeSpan.FromDays(1))
            {
                var logRetentionDays = 7; // Default 7 days
                if (settings.Any(s => s.Key == "LogRetentionDays") && 
                    int.TryParse(settings.First(s => s.Key == "LogRetentionDays").Value, out var days) && days > 0)
                {
                    logRetentionDays = days;
                }
                LoggerService.Info($"Cleaning logs older than {logRetentionDays} days");
                CleanOldLogs(logRetentionDays);
                _lastLogCleanup = nowUtc;
            }

            // Clean old sensor data daily - DELETE data older than retention period
            if (nowUtc - _lastSensorCleanup >= TimeSpan.FromDays(1))
            {
                var sensorRetentionDays = DefaultSensorRetentionDays;
                if (settings.Any(s => s.Key == "SensorDataRetentionDays") && 
                    int.TryParse(settings.First(s => s.Key == "SensorDataRetentionDays").Value, out var sdDays) && sdDays > 0)
                {
                    sensorRetentionDays = sdDays;
                }
                
                var cutoffDate = DateTime.Now.AddDays(-sensorRetentionDays);
                LoggerService.Info($"Cleaning sensor data older than {sensorRetentionDays} days (before {cutoffDate})");
                
                // Delete old data in batches to avoid locking the database
                var deletedCount = await db.Deleteable<SensorData>()
                    .Where(s => s.Timestamp < cutoffDate)
                    .ExecuteCommandAsync();
                    
                if (deletedCount > 0)
                {
                    LoggerService.Info($"Deleted {deletedCount} old sensor data entries.");
                    // Run VACUUM to reclaim space (SQLite specific)
                    try
                    {
                        await db.Ado.ExecuteCommandAsync("VACUUM;");
                        LoggerService.Info("Database vacuumed successfully.");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Warn($"Failed to vacuum database: {ex.Message}");
                    }
                }
                
                _lastSensorCleanup = nowUtc;
            }
        }

        private void CleanOldLogs(int retentionDays)
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logDir)) return;
            
            var cutoffDate = DateTime.Now.AddDays(-retentionDays);
            var files = Directory.GetFiles(logDir, "*.log", SearchOption.AllDirectories);
            var deletedCount = 0;
            
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }
                catch
                {
                    // ignored
                }
            }
            
            if (deletedCount > 0)
            {
                LoggerService.Info($"Deleted {deletedCount} old log files.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
} 