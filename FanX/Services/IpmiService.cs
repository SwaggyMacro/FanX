using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FanX.Models;

namespace FanX.Services;

public class IpmiService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _ipmiToolPath;

    public IpmiService(IServiceScopeFactory scopeFactory, IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _ipmiToolPath = Path.Combine(env.ContentRootPath, "Lib", "bmc", "ipmitool.exe");
        }
        else
        {
            _ipmiToolPath = "ipmitool"; // Assume it's in PATH on other systems
        }
    }

    private async Task<(bool success, string output, string error)> ExecuteIpmiToolAsync(string command)
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IpmiConfigService>();
        
        var config = await configService.GetConfigAsync();
        if (string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
        {
            const string errorMsg = "IPMI configuration is not set.";
            LoggerService.Warn(errorMsg);
            return (false, string.Empty, errorMsg);
        }

        var arguments = $"-I lanplus -H {config.Host} -U {config.Username} -P \"{config.Password}\" {command}";
        LoggerService.Info($"Executing IPMI command: ipmitool {arguments.Replace(config.Password, "\"********\"")}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _ipmiToolPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                LoggerService.Info($"IPMI command successful. Output: {(!string.IsNullOrWhiteSpace(output) ? output.Trim() : "None")}");
            }
            else
            {
                LoggerService.Error($"IPMI command failed with exit code {process.ExitCode}. Error: {error.Trim()}");
            }

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Failed to execute ipmitool from path '{_ipmiToolPath}'.";
            LoggerService.Fatal(errorMsg, ex);
            return (false, string.Empty, $"{errorMsg} Exception: {ex.Message}");
        }
    }

    public async Task<(bool success, string output, string error)> GetSdrListAsync()
    {
        return await ExecuteIpmiToolAsync("sdr list");
    }

    public async Task<(bool success, string output, string error)> PowerControlAsync(string action)
    {
        if (action is not ("on" or "off" or "cycle" or "reset" or "status"))
        {
            return (false, string.Empty, "Invalid power action. Must be one of: on, off, cycle, reset, status.");
        }
        return await ExecuteIpmiToolAsync($"power {action}");
    }

    public async Task<(bool success, string output, string error)> SetFanModeAsync(string mode)
    {
        if (mode is not ("Standard" or "Full"))
        {
            return (false, string.Empty, "Invalid fan mode. Must be 'Standard' or 'Full'.");
        }
        
        // This is a common command for Dell servers. May need to be adapted for other vendors.
        // 0x00 = Standard, 0x01 = Full Speed
        var modeValue = mode == "Standard" ? "0x00" : "0x01";
        return await ExecuteIpmiToolAsync($"raw 0x30 0xce 0x00 0x16 0x05 0x00 0x00 0x00 0x07 0x00 {modeValue}");
    }
    
    public async Task<(bool success, string output, string error)> SetAllFansSpeedAsync(int speedPercent)
    {
        if (speedPercent is < 0 or > 100)
        {
            return (false, string.Empty, "Invalid fan speed percentage. Must be between 0 and 100.");
        }
        
        var (manualSuccess, _, manualError) = await SetManualFanControlAsync();
        if (!manualSuccess)
        {
            return (false, string.Empty, $"Failed to set fan control to manual: {manualError}");
        }

        var hexSpeed = speedPercent.ToString("X2");
        return await ExecuteIpmiToolAsync($"raw 0x30 0x30 0x02 0xff 0x{hexSpeed}");
    }

    public async Task<(bool success, string output, string error)> SetIndividualFanSpeedAsync(string fanIdHex, int speedPercent)
    {
        if (speedPercent is < 0 or > 100)
        {
            return (false, string.Empty, "Invalid fan speed percentage. Must be between 0 and 100.");
        }
        
        var hexSpeed = speedPercent.ToString("X2");
        return await ExecuteIpmiToolAsync($"raw 0x30 0x30 0x02 {fanIdHex} 0x{hexSpeed}");
    }

    public async Task<(bool success, string output, string error)> SetManualFanControlAsync()
    {
        return await ExecuteIpmiToolAsync("raw 0x30 0x30 0x01 0x00");
    }
    
    public async Task<(bool success, string output, string error)> SetAutomaticFanControlAsync()
    {
        return await ExecuteIpmiToolAsync("raw 0x30 0x30 0x01 0x01");
    }
    
    public IEnumerable<SensorData> ParseFullSdrOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) yield break;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var timestamp = DateTime.Now;
        var nameCounts = new Dictionary<string, int>();
        var fanIndex = 0;

        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 3) continue;

            var name = parts[0].Trim();
            var readingStr = parts[1].Trim();
            var status = parts[2].Trim();

            if (status.Equals("ns", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
                readingStr.Contains("Not Readable", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Handle duplicate sensor names by appending a counter
            if (name.Equals("Temp", StringComparison.OrdinalIgnoreCase))
            {
                var count = nameCounts.GetValueOrDefault("Temp", 0) + 1;
                nameCounts["Temp"] = count;
                name = $"CPU{count} Temp";
            }
            else if (nameCounts.ContainsKey(name))
            {
                nameCounts[name]++;
                name = $"{name}-{nameCounts[name]}";
            }
            else
            {
                if (lines.Count(l => l.Split('|')[0].Trim() == name) > 1)
                {
                    nameCounts[name] = 1;
                    name = $"{name}-1";
                }
            }

            string? type = null;
            string unit = string.Empty;
            string? sensorId = null;

            if (name.Contains("temp", StringComparison.OrdinalIgnoreCase))
            {
                type = "Temperature";
                unit = "°C";
            }
            else if (name.Contains("fan", StringComparison.OrdinalIgnoreCase) && !name.Contains("redundancy", StringComparison.OrdinalIgnoreCase))
            {
                type = "Fan";
                unit = "RPM";
                sensorId = $"0x{fanIndex:x2}";
                fanIndex++;
            }
            else if (name.Contains("pwr", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("power", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("current", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("voltage", StringComparison.OrdinalIgnoreCase)
                    )
            {
                type = "Power";
                if (readingStr.Contains("Watts", StringComparison.OrdinalIgnoreCase)) unit = "Watts";
                else if (readingStr.Contains("Amps", StringComparison.OrdinalIgnoreCase)) unit = "Amps";
                else if (readingStr.Contains("Volts", StringComparison.OrdinalIgnoreCase)) unit = "Volts";
            }

            if (type == null) continue;

            var readingMatch = Regex.Match(readingStr, @"(-?\d+(?:\.\d+)?)");
            if (readingMatch.Success && double.TryParse(readingMatch.Groups[1].Value, out var reading))
            {
                yield return new SensorData
                {
                    Timestamp = timestamp,
                    SensorId = sensorId ?? name, // Use fan index for fans, otherwise name
                    SensorType = type,
                    SensorName = name,
                    Reading = reading,
                    Unit = unit,
                    Pwm = null // PWM is not available in this output format
                };
            }
        }
    }
} 