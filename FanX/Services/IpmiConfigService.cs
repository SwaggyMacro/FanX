using FanX.Models;

namespace FanX.Services;

public class IpmiConfigService
{
    private readonly DatabaseService _dbService;

    public IpmiConfigService(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    public async Task<IpmiConfig> GetConfigAsync()
    {
        var config = await _dbService.Db.Queryable<IpmiConfig>().FirstAsync();
        return config ?? new IpmiConfig();
    }

    public async Task SaveConfigAsync(IpmiConfig config)
    {
        var existingConfig = await _dbService.Db.Queryable<IpmiConfig>().FirstAsync();
        if (existingConfig != null)
        {
            config.Id = existingConfig.Id;
            await _dbService.Db.Updateable(config).ExecuteCommandAsync();
        }
        else
        {
            await _dbService.Db.Insertable(config).ExecuteCommandAsync();
        }
    }
} 