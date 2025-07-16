using SqlSugar;

namespace FanX.Models;

[SugarTable("Users")]
public class User
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 50, IsNullable = false)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = false)]
    public string Email { get; set; } = string.Empty;

    [SugarColumn(Length = 255, IsNullable = false)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(Length = 50)] public string? Role { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;

    [SugarColumn(IsNullable = true)]
    public string? SessionToken { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? SessionExpiresAt { get; set; }
}