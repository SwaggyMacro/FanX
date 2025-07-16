using SqlSugar;

namespace FanX.Models;

[SugarTable("NotificationRules")]
public class NotificationRule
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string Name { get; set; } = "New Rule";

    public int SortOrder { get; set; } = 0;

    // Cooldown in minutes
    public int FrequencyMinutes { get; set; } = 10;

    public string? Template { get; set; } = "🚨 Alert: {RuleName} triggered. Conditions met: {ConditionsSummary}";
    
    // Navigation property for conditions
    [SugarColumn(IsIgnore = true)]
    public List<NotificationCondition> Conditions { get; set; } = new();
}
