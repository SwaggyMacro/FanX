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
                .OrderByDescending(r => r.TargetFanSpeedPercent)
                .ToList();

            FanControlRule? triggeredRule = null;
            foreach (var rule in rules)
            {
                bool result = false;
                for (int i = 0; i < rule.Conditions.Count; i++)
                {
                    var condition = rule.Conditions[i];
                    var sensor = sensorData.FirstOrDefault(s => s.SensorName != null && s.SensorName.Equals(condition.SensorName, StringComparison.OrdinalIgnoreCase));
                    var current = sensor != null && IsConditionMet(sensor.Reading, condition.Threshold, condition.Operator);
                    if (i == 0)
                    {
                        result = current;
                }
                    else
                    {
                        if (condition.Connector == ConditionLogicalOperator.And)
                            result = result && current;
                        else
                            result = result || current;
                    }
                }
                if (result)
                {
                    triggeredRule = rule;
                    break;
                }
            }

            if (triggeredRule != null)
            {
                var metConditionsDescriptions = new List<string>();
                foreach (var condition in triggeredRule.Conditions)
                {
                    var sensor = sensorData.FirstOrDefault(s => s.SensorName != null && s.SensorName.Equals(condition.SensorName, StringComparison.OrdinalIgnoreCase));
                    if (sensor != null)
                    {
                        var opString = condition.Operator switch
                        {
                            TriggerOperator.GreaterThan => ">",
                            TriggerOperator.LessThan => "<",
                            TriggerOperator.EqualTo => "==",
                            _ => "?"
                        };
                        metConditionsDescriptions.Add($"'{sensor.SensorName}' ({sensor.Reading:F1}{sensor.Unit}) {opString} {condition.Threshold}");
                    }
                }
                
                var logMessage = $"Fan control rule '{triggeredRule.Name}' triggered. Conditions met: [{string.Join(" AND ", metConditionsDescriptions)}]. " +
                                 $"Setting fans [{string.Join(", ", triggeredRule.TargetFanNames)}] to {triggeredRule.TargetFanSpeedPercent}%.";
                
                LoggerService.Info(logMessage);
                
                await ipmiService.SetManualFanControlAsync();

                foreach (var fanName in triggeredRule.TargetFanNames)
                {
                    var fanSensor = sensorData.FirstOrDefault(s => s.SensorName == fanName);
                    if (fanSensor?.SensorId != null)
                    {
                        await ipmiService.SetIndividualFanSpeedAsync(fanSensor.SensorId, triggeredRule.TargetFanSpeedPercent);
                    }
                }
                    }
                    else
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