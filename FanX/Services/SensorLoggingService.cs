using FanX.Models;

namespace FanX.Services
{
    public class SensorLoggingService : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private Timer? _timer;

        public SensorLoggingService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            LoggerService.Info("Sensor Logging Service is starting.");
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            return Task.CompletedTask;
        }

        private async void DoWork(object? state)
        {
            LoggerService.Info("Sensor Logging Service is working.");
            using var scope = _serviceProvider.CreateScope();
            var ipmiService = scope.ServiceProvider.GetRequiredService<IpmiService>();
            var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var fanControlService = scope.ServiceProvider.GetRequiredService<FanControlService>();
            
            var (success, output, error) = await ipmiService.GetSdrListAsync();

            if (!success)
            {
                LoggerService.Error($"Failed to get SDR list: {error}");
                return;
            }
            
            var sensorData = ipmiService.ParseFullSdrOutput(output).ToList();
            
            if (sensorData.Any())
            {
                await dbService.Db.Insertable(sensorData).ExecuteCommandAsync();
                LoggerService.Info($"Logged {sensorData.Count} sensor readings.");
                
                // Check for notifications
                await notificationService.CheckAndSendNotificationsAsync(sensorData);

                // Adjust fan speed based on rules
                await AdjustFanSpeedBasedOnRules(fanControlService, ipmiService, sensorData);
            }
        }

        private async Task AdjustFanSpeedBasedOnRules(FanControlService fanControlService, IpmiService ipmiService, List<SensorData> sensorData)
        {
            var fanControlMode = await fanControlService.GetFanControlModeAsync();

            switch (fanControlMode)
            {
                case FanControlMode.Manual:
                    // When in Manual mode, do nothing. Let the user's manual settings persist.
                    return;
                case FanControlMode.Automatic:
                    // When in Automatic mode, ensure the system's fan control is set to automatic.
                    await ipmiService.SetAutomaticFanControlAsync();
                    return;
            }

            // If we reach here, the mode is Smart.
            var rules = (await fanControlService.GetRulesAsync())
                .Where(r => r.IsEnabled && r.Conditions.Any())
                .OrderBy(r => r.SortOrder)
                .ToList();
            bool anyTriggered = false;
            // Switch to manual mode once before applying rules
            await ipmiService.SetManualFanControlAsync();
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
                            await ipmiService.SetIndividualFanSpeedAsync(fanSensor.SensorId, rule.TargetFanSpeedPercent);
                        }
                    }
                }
            }
            if (!anyTriggered)
            {
                LoggerService.Info("No fan control rule triggered. Setting fans to automatic.");
                await ipmiService.SetAutomaticFanControlAsync();
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
    }
}
