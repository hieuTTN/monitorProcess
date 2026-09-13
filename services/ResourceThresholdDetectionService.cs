using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Phát hiện tiến trình có CPU% hoặc RAM% vượt ngưỡng cấu hình - dấu hiệu
    /// hành vi bất thường (ví dụ CryptoMiner tiêu tốn CPU liên tục ở mức cao).
    ///
    /// Ngưỡng RAM tính theo % so với TỔNG RAM hệ thống (không phải RAM tối đa
    /// của tiến trình), lấy từ ResourceUsage.MemoryPercent đã tính sẵn trong
    /// ResourceMonitorService.
    /// </summary>
    public class ResourceThresholdDetectionService
    {
        private readonly string _configPath;
        private ThresholdConfig _config = new();

        public ResourceThresholdDetectionService(string configPath)
        {
            _configPath = configPath;
            Load();
        }

        public void Load()
        {
            if (!File.Exists(_configPath))
            {
                _config = new ThresholdConfig();
                return;
            }

            var json = File.ReadAllText(_configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _config = JsonSerializer.Deserialize<ThresholdConfig>(json, options) ?? new ThresholdConfig();
        }

        public void Save(ThresholdConfig config)
        {
            _config = config;

            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(config, options));
        }

        public ThresholdConfig GetConfig() => _config;

        /// <summary>
        /// Kiểm tra 1 tiến trình. Có thể trả về TỐI ĐA 2 cảnh báo cùng lúc
        /// (vừa vượt ngưỡng CPU vừa vượt ngưỡng RAM), hoặc danh sách rỗng nếu
        /// không vi phạm gì / cả 2 ngưỡng đều đang tắt.
        /// </summary>
        public List<AlertLog> Check(ProcessInfo process, double cpuPercent, double memoryPercent)
        {
            var alerts = new List<AlertLog>();

            if (_config.EnableCpuThreshold && cpuPercent >= _config.CpuThresholdPercent)
            {
                alerts.Add(new AlertLog
                {
                    Pid = process.Pid,
                    ProcessName = process.Name,
                    ExePath = process.ExePath,
                    UserName = process.UserName,
                    Type = AlertType.HighCpuUsage,
                    Severity = AlertSeverity.Critical,
                    Message = $"CPU sử dụng {cpuPercent:0.0}% vượt ngưỡng cấu hình " +
                              $"({_config.CpuThresholdPercent:0}%) - dấu hiệu bất thường (VD: CryptoMiner)"
                });
            }

            if (_config.EnableRamThreshold && memoryPercent >= _config.RamThresholdPercent)
            {
                alerts.Add(new AlertLog
                {
                    Pid = process.Pid,
                    ProcessName = process.Name,
                    ExePath = process.ExePath,
                    UserName = process.UserName,
                    Type = AlertType.HighRamUsage,
                    Severity = AlertSeverity.Warning,
                    Message = $"RAM sử dụng {memoryPercent:0.0}% tổng RAM hệ thống, vượt ngưỡng cấu hình " +
                              $"({_config.RamThresholdPercent:0}%)"
                });
            }

            return alerts;
        }
    }
}