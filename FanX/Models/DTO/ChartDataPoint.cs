namespace FanX.Models.DTO
{
    /// <summary>
    /// Lightweight DTO for chart data to minimize SignalR payload
    /// </summary>
    public class ChartDataPoint
    {
        public DateTime T { get; set; }  // Timestamp (short name to reduce JSON size)
        public double V { get; set; }    // Value/Reading
        public string? N { get; set; }   // SensorName
        public string? S { get; set; }   // SensorType
    }
}
