using SqlSugar;

namespace FanX.Models;

public class AppSetting
{
    [SugarColumn(IsPrimaryKey = true)]
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
} 