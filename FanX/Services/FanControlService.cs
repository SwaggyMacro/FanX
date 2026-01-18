using System.Text.Json;
using FanX.Models;

namespace FanX.Services
{
    public class FanControlService
    {
        private readonly DatabaseService _dbService;

        public FanControlService(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        public async Task<List<FanControlRule>> GetRulesAsync(int? ipmiConfigId = null)
        {
            var rules = await _dbService.Db.Queryable<FanControlRule>()
                .OrderBy(r => r.SortOrder)
                .ToListAsync();
            if (rules.Count == 0) return rules;
            var ruleIds = rules.Select(r => r.Id).ToList();
            var allConditions = await _dbService.Db.Queryable<FanControlCondition>().Where(c => ruleIds.Contains(c.RuleId)).ToListAsync();
            foreach(var rule in rules)
            {
                rule.Conditions = allConditions.Where(c => c.RuleId == rule.Id).ToList();
                rule.TargetFanNames = JsonSerializer.Deserialize<List<string>>(rule.TargetFanNamesJson) ?? [];
                rule.TargetIpmiConfigIds = string.IsNullOrWhiteSpace(rule.TargetIpmiConfigIdsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<int>>(rule.TargetIpmiConfigIdsJson) ?? [];
            }
            if (ipmiConfigId.HasValue)
            {
                return rules.Where(r => r.TargetIpmiConfigIds.Count == 0 || r.TargetIpmiConfigIds.Contains(ipmiConfigId.Value)).ToList();
            }
            return rules;
        }
        
        public async Task<FanControlRule?> GetRuleByIdAsync(int id)
        {
            var rule = await _dbService.Db.Queryable<FanControlRule>().InSingleAsync(id);
            if (rule != null)
            {
                rule.Conditions = await _dbService.Db.Queryable<FanControlCondition>().Where(c => c.RuleId == id).ToListAsync();
                rule.TargetFanNames = JsonSerializer.Deserialize<List<string>>(rule.TargetFanNamesJson) ?? [];
                rule.TargetIpmiConfigIds = string.IsNullOrWhiteSpace(rule.TargetIpmiConfigIdsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<int>>(rule.TargetIpmiConfigIdsJson) ?? [];
            }
            return rule;
        }

        public async Task<bool> SaveRuleAsync(FanControlRule rule)
        {
            try
            {
                await _dbService.Db.Ado.BeginTranAsync();

                rule.TargetFanNamesJson = JsonSerializer.Serialize(rule.TargetFanNames);
                rule.TargetIpmiConfigIds ??= [];
                rule.TargetIpmiConfigIdsJson = JsonSerializer.Serialize(rule.TargetIpmiConfigIds);

                if (rule.Id == 0)
                {
                    rule.Id = await _dbService.Db.Insertable(rule).ExecuteReturnIdentityAsync();
                }
                else
                {
                    await _dbService.Db.Updateable(rule).ExecuteCommandAsync();
                }

                await _dbService.Db.Deleteable<FanControlCondition>().Where(c => c.RuleId == rule.Id).ExecuteCommandAsync();

                if (rule.Conditions.Any())
                {
                    rule.Conditions.ForEach(c => { c.RuleId = rule.Id; c.Id = 0; });
                    await _dbService.Db.Insertable(rule.Conditions).ExecuteCommandAsync();
                }

                await _dbService.Db.Ado.CommitTranAsync();
                return true;
            }
            catch (Exception ex)
            {
                await _dbService.Db.Ado.RollbackTranAsync();
                LoggerService.Error("Failed to save fan control rule.", ex);
                return false;
            }
        }
        
        public async Task<bool> DeleteRuleAsync(int id)
        {
            try
            {
                await _dbService.Db.Ado.BeginTranAsync();
                await _dbService.Db.Deleteable<FanControlCondition>().Where(c => c.RuleId == id).ExecuteCommandAsync();
                await _dbService.Db.Deleteable<FanControlRule>().In(id).ExecuteCommandAsync();
                await _dbService.Db.Ado.CommitTranAsync();
                return true;
            }
            catch (Exception ex)
            {
                await _dbService.Db.Ado.RollbackTranAsync();
                LoggerService.Error($"Failed to delete fan control rule with id {id}", ex);
                return false;
            }
        }

        public async Task<FanControlMode> GetFanControlModeAsync()
        {
            var setting = await _dbService.Db.Queryable<AppSetting>().In(nameof(FanControlMode)).SingleAsync();
            if (setting != null && Enum.TryParse<FanControlMode>(setting.Value, out var mode))
            {
                return mode;
            }
            return FanControlMode.Automatic;
        }

        public async Task SetFanControlModeAsync(FanControlMode mode)
        {
            var setting = new AppSetting
            {
                Key = nameof(FanControlMode),
                Value = mode.ToString()
            };
            await _dbService.Db.Storageable(setting).ExecuteCommandAsync();
        }
    }
}
