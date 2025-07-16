using SqlSugar;

namespace FanX.Models;

public class NotificationHistory
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public int TriggerId { get; set; }

    public DateTime TriggeredAt { get; set; }
} 