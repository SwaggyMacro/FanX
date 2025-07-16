using SqlSugar;

namespace FanX.Models;

public enum TriggerOperator
{
    GreaterThan,
    LessThan,
    EqualTo
}

[SugarTable("NotificationConditions")]
public class NotificationCondition
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }
    
    [SugarColumn(ColumnName = "RuleId")]
    public int RuleId { get; set; }

    public string? SensorName { get; set; }

    public TriggerOperator Operator { get; set; } = TriggerOperator.GreaterThan;

    public double Threshold { get; set; }
} 