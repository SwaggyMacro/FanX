using System.Text;
using FanX.Models;
using Newtonsoft.Json;
using SqlSugar;
using MimeKit;

namespace FanX.Services;

public class NotificationService
{
    private readonly DatabaseService _dbService;
    private readonly IHttpClientFactory _httpClientFactory;

    public NotificationService(DatabaseService dbService, IHttpClientFactory httpClientFactory)
    {
        _dbService = dbService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<NotificationSetting> GetNotificationSettingAsync()
    {
        return await _dbService.Db.Queryable<NotificationSetting>().FirstAsync() ?? new NotificationSetting();
    }

    public async Task<bool> SaveNotificationSettingAsync(NotificationSetting setting)
    {
        return await _dbService.Db.Updateable(setting).ExecuteCommandHasChangeAsync();
    }

    public async Task<List<NotificationRule>> GetNotificationRulesAsync()
    {
        var rules = await _dbService.Db.Queryable<NotificationRule>().ToListAsync();
        if (rules.Any())
        {
            var ruleIds = rules.Select(r => r.Id).ToList();
            var allConditions = await _dbService.Db.Queryable<NotificationCondition>().Where(c => ruleIds.Contains(c.RuleId)).ToListAsync();
            foreach(var rule in rules)
            {
                rule.Conditions = allConditions.Where(c => c.RuleId == rule.Id).ToList();
            }
        }
        return rules;
    }

    public async Task<NotificationRule?> GetNotificationRuleByIdAsync(int id)
    {
        var rule = await _dbService.Db.Queryable<NotificationRule>().InSingleAsync(id);
        if (rule != null)
        {
            rule.Conditions = await _dbService.Db.Queryable<NotificationCondition>().Where(c => c.RuleId == id).ToListAsync();
        }
        return rule;
    }
    
    public async Task<bool> SaveNotificationRuleAsync(NotificationRule rule)
    {
        try
        {
            await _dbService.Db.Ado.BeginTranAsync();
            
            // Explicitly handle Insert vs. Update for the rule for more reliability
            if (rule.Id == 0)
            {
                // It's a new rule, insert it and get the new ID
                rule.Id = await _dbService.Db.Insertable(rule).ExecuteReturnIdentityAsync();
            }
            else
            {
                // It's an existing rule, update it
                await _dbService.Db.Updateable(rule).ExecuteCommandAsync();
            }

            // Delete existing conditions to re-add them
            await _dbService.Db.Deleteable<NotificationCondition>().Where(c => c.RuleId == rule.Id).ExecuteCommandAsync();

            if (rule.Conditions != null && rule.Conditions.Any())
            {
                // Prepare conditions for insertion
                rule.Conditions.ForEach(c => 
                {
                    c.RuleId = rule.Id;
                    c.Id = 0; // Force insert
                });
                await _dbService.Db.Insertable(rule.Conditions).ExecuteCommandAsync();
            }

            await _dbService.Db.Ado.CommitTranAsync();
            return true;
        }
        catch (Exception ex)
        {
            await _dbService.Db.Ado.RollbackTranAsync();
            LoggerService.Error("Failed to save notification rule.", ex);
            return false;
        }
    }

    public async Task<bool> DeleteNotificationRuleAsync(int id)
    {
        try
        {
            await _dbService.Db.Ado.BeginTranAsync();
            await _dbService.Db.Deleteable<NotificationCondition>().Where(c => c.RuleId == id).ExecuteCommandAsync();
            await _dbService.Db.Deleteable<NotificationRule>().In(id).ExecuteCommandAsync();
            await _dbService.Db.Ado.CommitTranAsync();
            return true;
        }
        catch (Exception ex)
        {
            await _dbService.Db.Ado.RollbackTranAsync();
            LoggerService.Error($"Failed to delete rule with id {id}", ex);
            return false;
        }
    }
    
    public async Task CheckAndSendNotificationsAsync(IEnumerable<SensorData> sensorReadings)
    {
        var settings = await GetNotificationSettingAsync();
        if (!settings.IsEnabled) return;

        var rules = await GetNotificationRulesAsync();
        if (!rules.Any()) return;

        var sensorReadingsList = sensorReadings.ToList();

        foreach (var rule in rules.Where(r => r.IsEnabled && r.Conditions.Any()))
        {
            var allConditionsMet = true;
            var conditionsSummary = new StringBuilder();

            foreach (var condition in rule.Conditions)
            {
                var sensorReading = sensorReadingsList.FirstOrDefault(s =>
                    string.Equals(s.SensorName, condition.SensorName, StringComparison.OrdinalIgnoreCase));

                if (sensorReading == null)
                {
                    // If a sensor in a condition is not found in the latest readings, the condition cannot be met.
                    allConditionsMet = false;
                    break;
                }

                var conditionMetThisCycle = IsConditionMet(sensorReading.Reading, condition.Threshold, condition.Operator);

                if (!conditionMetThisCycle)
                {
                    allConditionsMet = false;
                    break;
                }

                conditionsSummary.Append($"{sensorReading.SensorName} was {sensorReading.Reading:F1}{sensorReading.Unit}; ");
            }

            if (allConditionsMet)
            {
                await HandleTriggeredRule(rule, settings, conditionsSummary.ToString().TrimEnd(' ', ';'));
            }
        }
    }
    
    private async Task HandleTriggeredRule(NotificationRule rule, NotificationSetting settings, string conditionsSummary)
    {
        var lastTriggered = await _dbService.Db.Queryable<NotificationHistory>()
            .Where(h => h.TriggerId == rule.Id)
            .OrderBy(h => h.TriggeredAt, OrderByType.Desc)
            .FirstAsync();

        if (lastTriggered != null && lastTriggered.TriggeredAt.AddMinutes(rule.FrequencyMinutes) > DateTime.Now)
        {
            return;
        }

        var title = $"Rule '{rule.Name}' Fired";
        var message = FormatMessage(rule.Template, rule, conditionsSummary);

        LoggerService.Info($"Rule '{rule.Name}' fired. Sending notifications.");

        if (settings.WebhookEnabled && !string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            await SendWebhookAsync(settings, title, message);
        }

        if (settings.TelegramEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            await SendTelegramMessageAsync(settings.TelegramBotToken, settings.TelegramChatId, message);
        }
        
        if (settings.WeComBotEnabled && !string.IsNullOrWhiteSpace(settings.WeComBotKey))
        {
            await SendWeComBotAsync(settings.WeComBotKey, message);
        }
        // Email notifications
        if (settings.EmailEnabled && !string.IsNullOrWhiteSpace(settings.EmailRecipients))
        {
            await SendTestEmailAsync(settings, title, message);
        }

        await _dbService.Db.Insertable(new NotificationHistory { TriggerId = rule.Id, TriggeredAt = DateTime.Now }).ExecuteCommandAsync();
    }
    
    private bool IsConditionMet(double value, double threshold, TriggerOperator op)
    {
        return op switch
        {
            TriggerOperator.GreaterThan => value > threshold,
            TriggerOperator.LessThan => value < threshold,
            TriggerOperator.EqualTo => Math.Abs(value - threshold) < 0.001,
            _ => false,
        };
    }

    private string FormatMessage(string? template, NotificationRule rule, string conditionsSummary)
    {
        var messageTemplate = template;
        if (string.IsNullOrWhiteSpace(messageTemplate))
        {
            messageTemplate = "🚨 Alert: {RuleName} triggered. Conditions met: {ConditionsSummary}";
        }
        
        return messageTemplate
            .Replace("{RuleName}", rule.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{ConditionsSummary}", conditionsSummary, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> SendTestWebhookAsync(NotificationSetting settings, string title, string message)
    {
        if (!settings.WebhookEnabled || string.IsNullOrWhiteSpace(settings.WebhookUrl)) return false;
        return await SendWebhookAsync(settings, title, message);
    }

    public async Task<bool> SendTestTelegramAsync(NotificationSetting settings, string title, string message)
    {
        if (!settings.TelegramEnabled || string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId)) return false;
        return await SendTelegramMessageAsync(settings.TelegramBotToken, settings.TelegramChatId, $"{title}\n\n{message}");
    }

    public async Task<bool> SendTestWeComBotAsync(NotificationSetting settings, string title, string message)
    {
        if (!settings.WeComBotEnabled || string.IsNullOrWhiteSpace(settings.WeComBotKey)) return false;
        return await SendWeComBotAsync(settings.WeComBotKey, $"{title}\n\n{message}");
    }

    /// <summary>
    /// Sends a test email using the SMTP settings in NotificationSetting.
    /// </summary>
    public async Task<bool> SendTestEmailAsync(NotificationSetting settings, string subject, string body)
    {
        if (settings == null || !settings.EmailEnabled)
            return false;
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || settings.SmtpPort <= 0)
            return false;
        if (string.IsNullOrWhiteSpace(settings.EmailRecipients))
            return false;

        try
        {
            // Build email message
            var recipients = settings.EmailRecipients
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .ToList();
            var message = new MimeMessage();
            var fromAddress = !string.IsNullOrWhiteSpace(settings.SmtpUser)
                ? settings.SmtpUser.Trim()
                : recipients.First();
            message.From.Add(new MailboxAddress(fromAddress, fromAddress));
            foreach (var addr in recipients)
                message.To.Add(new MailboxAddress(addr, addr));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            // Send via MailKit for implicit SSL support
            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, MailKit.Security.SecureSocketOptions.SslOnConnect);
            if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
                await client.AuthenticateAsync(settings.SmtpUser, settings.SmtpPass ?? string.Empty);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            LoggerService.Error("Failed to send test email via MailKit.", ex);
            return false;
        }
    }

    private async Task<bool> SendWebhookAsync(NotificationSetting settings, string title, string message)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var method = new HttpMethod(settings.WebhookRequestMethod.ToString());
            
            var request = new HttpRequestMessage(method, settings.WebhookUrl);

            if (method != HttpMethod.Get && !string.IsNullOrWhiteSpace(settings.WebhookBodyTemplate))
            {
                var jsonPayload = settings.WebhookBodyTemplate
                    .Replace("{Title}", title, StringComparison.OrdinalIgnoreCase)
                    .Replace("{Message}", message, StringComparison.OrdinalIgnoreCase);
                
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                LoggerService.Error($"Webhook failed with status code {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LoggerService.Error("Error sending webhook.", ex);
            return false;
        }
    }

    private async Task<bool> SendTelegramMessageAsync(string botToken, string chatId, string text)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var payload = new { chat_id = chatId, text = text };
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                LoggerService.Error($"Telegram notification failed with status code {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LoggerService.Error("Failed to send Telegram notification.", ex);
            return false;
        }
    }
    
    private async Task<bool> SendWeComBotAsync(string key, string message)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key={key}";
            var payload = new
            {
                msgtype = "text",
                text = new { content = message }
            };
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                LoggerService.Error($"WeCom Bot notification failed with status code {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LoggerService.Error("Failed to send WeCom Bot notification.", ex);
            return false;
        }
    }
} 