using SqlSugar;

namespace FanX.Models;

public enum WebhookMethod
{
    POST,
    PUT,
    GET
}

public class NotificationSetting
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    public bool IsEnabled { get; set; }
    
    // Webhook
    public bool WebhookEnabled { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? WebhookUrl { get; set; }
    public WebhookMethod WebhookRequestMethod { get; set; } = WebhookMethod.POST;
    [SugarColumn(IsNullable = true)]
    public string? WebhookBodyTemplate { get; set; }
    
    // Telegram
    public bool TelegramEnabled { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? TelegramBotToken { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? TelegramChatId { get; set; }
    
    // WeCom
    public bool WeComBotEnabled { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? WeComBotKey { get; set; }

    // Email notification settings
    public bool EmailEnabled { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? EmailRecipients { get; set; }
   
    // SMTP server settings
    [SugarColumn(IsNullable = true)]
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    [SugarColumn(IsNullable = true)]
    public string? SmtpUser { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? SmtpPass { get; set; }
    public bool SmtpEnableSsl { get; set; } = true;
} 