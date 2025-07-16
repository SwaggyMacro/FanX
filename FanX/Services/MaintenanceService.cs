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

        public MaintenanceService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Run every second to support debug intervals
            _timer = new Timer(async void (_) => await DoWork(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        }

        private async Task DoWork()
        {
            var nowUtc = DateTime.UtcNow;
            LoggerService.Debug($"MaintenanceService executing at {nowUtc} UTC.");
            if (!_initialized)
            {
                // Skip cleanup on initial startup
                _initialized = true;
                _lastLogCleanup = nowUtc;
                _lastSensorCleanup = nowUtc;
                LoggerService.Debug("MaintenanceService initialization complete, skipping first cleanup.");
                return;
            }
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DatabaseService>().Db;
            var settings = await db.Queryable<AppSetting>().ToListAsync();

            // Periodic full log cleanup by days
            if (settings.Any(s => s.Key == "LogRetentionDays") && int.TryParse(settings.First(s => s.Key == "LogRetentionDays").Value, out var days) && days > 0)
            {
                if (nowUtc - _lastLogCleanup >= TimeSpan.FromDays(days))
                {
                    LoggerService.Info($"Clearing all logs (daily interval: {days} days)");
                    ClearAllLogs();
                    _lastLogCleanup = nowUtc;
                }
            }

            // Periodic full sensor data cleanup by days
            if (settings.Any(s => s.Key == "SensorDataRetentionDays") && int.TryParse(settings.First(s => s.Key == "SensorDataRetentionDays").Value, out var sdDays) && sdDays > 0)
            {
                if (nowUtc - _lastSensorCleanup >= TimeSpan.FromDays(sdDays))
                {
                    LoggerService.Info($"Clearing all sensor data (daily interval: {sdDays} days)");
                    var deletedCount = await db.Deleteable<SensorData>().ExecuteCommandAsync();
                    LoggerService.Info($"Deleted {deletedCount} sensor data entries (full clear).");
                    _lastSensorCleanup = nowUtc;
                }
            }
        }

        private void ClearAllLogs()
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logDir)) return;
            var files = Directory.GetFiles(logDir, "*.log", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try { File.Delete(file); }
                catch
                {
                    // ignored
                }
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