using FanX.Models;

namespace FanX.Services;

public class IpmiConfigService
{
    private const string ActiveConfigKey = "ActiveIpmiConfigId";
    private readonly DatabaseService _dbService;
    private readonly AppSettingService _appSettingService;

    public IpmiConfigService(DatabaseService dbService, AppSettingService appSettingService)
    {
        _dbService = dbService;
        _appSettingService = appSettingService;
    }

    public async Task<IpmiConfig> GetConfigAsync()
    {
        return await GetActiveConfigAsync();
    }

    public async Task<IpmiConfig> GetConfigByIdAsync(int id)
    {
        var config = await _dbService.Db.Queryable<IpmiConfig>().InSingleAsync(id);
        return config ?? new IpmiConfig();
    }

    public async Task<List<IpmiConfig>> GetConfigsAsync()
    {
        return await _dbService.Db.Queryable<IpmiConfig>().OrderBy(c => c.Id).ToListAsync();
    }

    public async Task<List<IpmiConfig>> GetEnabledConfigsAsync()
    {
        return await _dbService.Db.Queryable<IpmiConfig>()
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.Id)
            .ToListAsync();
    }

    public async Task<IpmiConfig> GetActiveConfigAsync()
    {
        var activeId = await GetActiveConfigIdAsync();
        IpmiConfig? config = null;
        if (activeId.HasValue)
        {
            config = await _dbService.Db.Queryable<IpmiConfig>().InSingleAsync(activeId.Value);
        }

        if (config == null)
        {
            config = await _dbService.Db.Queryable<IpmiConfig>().OrderBy(c => c.Id).FirstAsync();
            if (config != null)
            {
                await SetActiveConfigAsync(config.Id);
            }
        }

        return config ?? new IpmiConfig();
    }

    public async Task<int?> GetActiveConfigIdAsync()
    {
        var value = await _appSettingService.GetSettingAsync(ActiveConfigKey);
        return int.TryParse(value, out var id) ? id : null;
    }

    public async Task SetActiveConfigAsync(int configId)
    {
        await _appSettingService.SetSettingAsync(ActiveConfigKey, configId.ToString());
    }

    public async Task<IpmiConfig> SaveConfigAsync(IpmiConfig config)
    {
        if (config.Id != 0)
        {
            await _dbService.Db.Updateable(config).ExecuteCommandAsync();
        }
        else
        {
            config.Id = await _dbService.Db.Insertable(config).ExecuteReturnIdentityAsync();
        }
        return config;
    }
} 
