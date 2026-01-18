using FanX.Models;

namespace FanX.Services
{
    public class SensorDataService
    {
        private readonly IpmiService _ipmiService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly Dictionary<int, List<SensorData>> _cachedSensors = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public SensorDataService(IpmiService ipmiService, IServiceScopeFactory scopeFactory)
        {
            _ipmiService = ipmiService;
            _scopeFactory = scopeFactory;
        }

        public async Task<List<SensorData>> GetSensorsAsync(int? configId = null)
        {
            var (config, resolvedConfigId) = await ResolveConfigAsync(configId);
            if (_cachedSensors.TryGetValue(resolvedConfigId, out var cachedSensors))
            {
                return cachedSensors;
            }

            await _semaphore.WaitAsync();
            try
            {
                // Double-check lock pattern to ensure it's only loaded once
                if (_cachedSensors.TryGetValue(resolvedConfigId, out cachedSensors))
                {
                    return cachedSensors;
                }

                var (success, output, error) = await _ipmiService.GetSdrListAsync(config);
                List<SensorData> sensors = new();
                if (success)
                {
                    sensors = _ipmiService.ParseFullSdrOutput(output).ToList();
                    foreach (var sensor in sensors)
                    {
                        sensor.IpmiConfigId = resolvedConfigId;
                    }
                    _cachedSensors[resolvedConfigId] = sensors;
                    LoggerService.Info($"Successfully pre-loaded and cached {sensors.Count} sensors for config {resolvedConfigId}.");
                }
                else
                {
                    LoggerService.Error($"Failed to initialize SensorDataService cache: {error}");
                    // Do not cache on failure to allow retry after configuration changes
                }
            }
            finally
            {
                _semaphore.Release();
            }

            // Return cached if loaded successfully, else return empty list and retry next time
            return _cachedSensors.TryGetValue(resolvedConfigId, out cachedSensors) ? cachedSensors : new List<SensorData>();
        }

        public void ClearCache(int? configId = null)
        {
            if (configId.HasValue)
            {
                _cachedSensors.Remove(configId.Value);
            }
            else
            {
                _cachedSensors.Clear();
            }
            LoggerService.Info("SensorDataService cache cleared.");
        }

        private async Task<(IpmiConfig? config, int configId)> ResolveConfigAsync(int? configId)
        {
            using var scope = _scopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IpmiConfigService>();
            if (configId.HasValue && configId.Value != 0)
            {
                var config = await configService.GetConfigByIdAsync(configId.Value);
                return (config?.Id == 0 ? null : config, config?.Id ?? 0);
            }

            var activeConfig = await configService.GetConfigAsync();
            return (activeConfig?.Id == 0 ? null : activeConfig, activeConfig?.Id ?? 0);
        }
    }
}
