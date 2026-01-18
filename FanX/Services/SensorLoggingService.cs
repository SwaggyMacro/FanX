using FanX.Models;

namespace FanX.Services
{
    public class SensorLoggingService : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private Timer? _timer;
        private int _currentIntervalSeconds = 60; // Default 60 seconds
        private int _workExecutionCount = 0;
        private const int SettingsCheckInterval = 10; // Check settings every 10 executions

        public SensorLoggingService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            LoggerService.Info("Sensor Logging Service is starting.");
            
            // Load polling interval from settings
            using var scope = _serviceProvider.CreateScope();
            var settingService = scope.ServiceProvider.GetRequiredService<AppSettingService>();
            _currentIntervalSeconds = await settingService.GetIntSettingAsync("IpmiPollingIntervalSeconds", 60);
            
            LoggerService.Info($"IPMI polling interval set to {_currentIntervalSeconds} seconds.");
            
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(_currentIntervalSeconds));
        }

        private async void DoWork(object? state)
        {
            LoggerService.Info("Sensor Logging Service is working.");
            using var scope = _serviceProvider.CreateScope();
            var ipmiService = scope.ServiceProvider.GetRequiredService<IpmiService>();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var fanControlService = scope.ServiceProvider.GetRequiredService<FanControlService>();
            var ipmiConfigService = scope.ServiceProvider.GetRequiredService<IpmiConfigService>();
            
            // Check if interval has changed every N executions to reduce database load
            _workExecutionCount++;
            if (_workExecutionCount >= SettingsCheckInterval)
            {
                _workExecutionCount = 0;
                var settingService = scope.ServiceProvider.GetRequiredService<AppSettingService>();
                var newInterval = await settingService.GetIntSettingAsync("IpmiPollingIntervalSeconds", 60);
                if (newInterval != _currentIntervalSeconds && newInterval >= 10 && newInterval <= 3600)
                {
                    _currentIntervalSeconds = newInterval;
                    _timer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_currentIntervalSeconds));
                    LoggerService.Info($"IPMI polling interval changed to {_currentIntervalSeconds} seconds.");
                }
            }
            
            var configs = await ipmiConfigService.GetEnabledConfigsAsync();
            if (!configs.Any())
            {
                LoggerService.Warn("No enabled IPMI configs found. Skipping sensor logging.");
                return;
            }

            foreach (var config in configs)
            {
                if (config.Id == 0)
                {
                    LoggerService.Warn("Skipping IPMI config with missing identifier.");
                    continue;
                }

                var (success, output, error) = await ipmiService.GetSdrListAsync(config);

                if (!success)
                {
                    LoggerService.Error($"Failed to get SDR list for IPMI config '{GetConfigLabel(config)}': {error}");
                    continue;
                }
                
                var sensorData = ipmiService.ParseFullSdrOutput(output).ToList();
                foreach (var reading in sensorData)
                {
                    reading.IpmiConfigId = config.Id;
                }
                
                if (sensorData.Any())
                {
                    await dbService.Db.Insertable(sensorData).ExecuteCommandAsync();
                    LoggerService.Info($"Logged {sensorData.Count} sensor readings for IPMI config '{GetConfigLabel(config)}'.");
                    
                    // Check for notifications
                    await notificationService.CheckAndSendNotificationsAsync(sensorData);

                    // Adjust fan speed based on rules
                    await AdjustFanSpeedBasedOnRules(fanControlService, ipmiService, sensorData, config);
                }
            }
        }

        private async Task AdjustFanSpeedBasedOnRules(FanControlService fanControlService, IpmiService ipmiService, List<SensorData> sensorData, IpmiConfig config)
        {
            var fanControlMode = await fanControlService.GetFanControlModeAsync();

            switch (fanControlMode)
            {
                case FanControlMode.Manual:
                    // When in Manual mode, do nothing. Let the user's manual settings persist.
                    return;
                case FanControlMode.Automatic:
                    // When in Automatic mode, ensure the system's fan control is set to automatic.
                    await ipmiService.SetAutomaticFanControlAsync(config);
                    return;
            }

            // If we reach here, the mode is Smart.
            var rules = (await fanControlService.GetRulesAsync(config.Id))
                .Where(r => r.IsEnabled && r.Conditions.Any())
                .OrderBy(r => r.SortOrder)
                .ToList();
            bool anyTriggered = false;
            // Switch to manual mode once before applying rules
            await ipmiService.SetManualFanControlAsync(config);
            foreach (var rule in rules)
            {
                bool match = false;
                for (int i = 0; i < rule.Conditions.Count; i++)
                {
                    var cond = rule.Conditions[i];
                    var sensor = sensorData.FirstOrDefault(s => s.SensorName != null && s.SensorName.Equals(cond.SensorName, StringComparison.OrdinalIgnoreCase));
                    var current = sensor != null && IsConditionMet(sensor.Reading, cond.Threshold, cond.Operator);
                    if (i == 0)
                        match = current;
                    else if (cond.Connector == ConditionLogicalOperator.And)
                        match &= current;
                    else
                        match |= current;
                }
                if (match)
                {
                    anyTriggered = true;
                    foreach (var fanName in rule.TargetFanNames)
                    {
                        var fanSensor = sensorData.FirstOrDefault(s => s.SensorName != null && s.SensorName.Equals(fanName, StringComparison.OrdinalIgnoreCase));
                        if (fanSensor?.SensorId != null)
                        {
                            LoggerService.Info($"Rule '{rule.Name}' triggered. Setting fan '{fanName}' to {rule.TargetFanSpeedPercent}%.");
                            await ipmiService.SetIndividualFanSpeedAsync(fanSensor.SensorId, rule.TargetFanSpeedPercent, config);
                        }
                    }
                }
            }
            if (!anyTriggered)
            {
                LoggerService.Info("No fan control rule triggered. Setting fans to automatic.");
                await ipmiService.SetAutomaticFanControlAsync(config);
            }
        }

        private bool IsConditionMet(double value, double threshold, TriggerOperator op)
                         {
            return op switch
            {
                TriggerOperator.GreaterThan => value > threshold,
                TriggerOperator.LessThan => value < threshold,
                TriggerOperator.EqualTo => Math.Abs(value - threshold) < 0.001,
                _ => false,
            };
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            LoggerService.Info("Sensor Logging Service is stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private static string GetConfigLabel(IpmiConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.Name))
            {
                return config.Name;
            }

            return string.IsNullOrWhiteSpace(config.Host) ? $"ID {config.Id}" : config.Host;
        }
    }
}
