using SqlSugar;

namespace FanX.Models
{
    [SugarTable("SensorData")]
    public class SensorData
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; }

        public string? SensorId { get; set; }

        [SugarColumn(Length = 50)]
        public string? SensorType { get; set; } = string.Empty;

        [SugarColumn(Length = 100)]
        public string? SensorName { get; set; } = string.Empty;

        public double Reading { get; set; }

        [SugarColumn(Length = 20)]
        public string? Unit { get; set; } = string.Empty;
        
        [SugarColumn(IsNullable = true)]
        public double? Pwm { get; set; }
    }
} 