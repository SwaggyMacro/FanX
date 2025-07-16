using SqlSugar;

namespace FanX.Models
{
    public enum ConditionLogicalOperator
    {
        And,
        Or
    }

    [SugarTable("FanControlConditions")]
    public class FanControlCondition
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(ColumnName = "RuleId")]
        public int RuleId { get; set; }

        public string? SensorName { get; set; }

        public TriggerOperator Operator { get; set; } = TriggerOperator.GreaterThan;

        public double Threshold { get; set; }

        public ConditionLogicalOperator Connector { get; set; } = ConditionLogicalOperator.And;
    }
} 