using FanX.Models;

namespace FanX.Services
{
    public class SensorDataService
    {
        private readonly IpmiService _ipmiService;
        private List<SensorData>? _cachedSensors;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public SensorDataService(IpmiService ipmiService)
        {
            _ipmiService = ipmiService;
        }

        public async Task<List<SensorData>> GetSensorsAsync()
        {
            if (_cachedSensors != null)
            {
                return _cachedSensors;
            }

            await _semaphore.WaitAsync();
            try
            {
                // Double-check lock pattern to ensure it's only loaded once
                if (_cachedSensors != null)
                {
                    return _cachedSensors;
                }

                var (success, output, error) = await _ipmiService.GetSdrListAsync();
                List<SensorData> sensors = new();
                if (success)
                {
                    sensors = _ipmiService.ParseFullSdrOutput(output).ToList();
                    _cachedSensors = sensors;
                    LoggerService.Info($"Successfully pre-loaded and cached {_cachedSensors.Count} sensors.");
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
            return _cachedSensors ?? new List<SensorData>();
        }

        public void ClearCache()
        {
            _cachedSensors = null;
            LoggerService.Info("SensorDataService cache cleared.");
        }
    }
}
