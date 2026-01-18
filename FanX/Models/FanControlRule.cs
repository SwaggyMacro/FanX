using SqlSugar;

namespace FanX.Models;

[SugarTable("FanControlRules")]
public class FanControlRule
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string Name { get; set; } = string.Empty;

    // Field for custom sort order
    public int SortOrder { get; set; }

    public int TargetFanSpeedPercent { get; set; }

    public string TargetFanNamesJson { get; set; } = "[]";

    public string TargetIpmiConfigIdsJson { get; set; } = "[]";

    [SugarColumn(IsIgnore = true)]
    public List<string> TargetFanNames { get; set; } = new();

    [SugarColumn(IsIgnore = true)]
    public List<int> TargetIpmiConfigIds { get; set; } = new();
    
    [SugarColumn(IsIgnore = true)]
    public List<FanControlCondition> Conditions { get; set; } = new();
}
