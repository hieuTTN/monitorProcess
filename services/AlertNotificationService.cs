using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using monitorProcess.controllers;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Điều phối gửi cảnh báo qua Telegram/Email khi phát hiện tiến trình bất
    /// thường. Tương ứng chức năng 10: Cảnh báo qua Email/Telegram Bot.
    ///
    /// Có cơ chế chống spam: mỗi cặp (PID + nội dung cảnh báo) chỉ gửi thông
    /// báo MỘT LẦN DUY NHẤT, không gửi lặp lại ở các chu kỳ quét 3 giây tiếp
    /// theo dù tiến trình đó vẫn còn tồn tại và vẫn vi phạm.
    /// </summary>
    public class AlertNotificationService
    {
        private readonly string _configPath;
        private readonly TelegramNotifierService _telegram = new();
        private readonly EmailNotifierService _email = new();

        private NotificationConfig _config = new();
        private readonly HashSet<string> _notifiedKeys = new();

        public AlertNotificationService(string configPath)
        {
            _configPath = configPath;
            LoadConfig();
        }

        public void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                _config = new NotificationConfig();
                return;
            }

            var json = File.ReadAllText(_configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _config = JsonSerializer.Deserialize<NotificationConfig>(json, options) ?? new NotificationConfig();
        }

        public void SaveConfig(NotificationConfig config)
        {
            _config = config;

            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(config, options));
        }

        public NotificationConfig GetConfig() => _config;

        /// <summary>
        /// Gửi thông báo cho các cảnh báo MỚI (chưa từng gửi). Không throw ra
        /// ngoài dù gửi thất bại - không muốn 1 lần gửi lỗi làm gãy vòng quét chính.
        /// </summary>
        public async Task NotifyNewAlertsAsync(IEnumerable<ProcessRowViewModel> rows)
        {
            if (!_config.EnableTelegram && !_config.EnableEmail)
                return;

            var newAlerts = new List<ProcessRowViewModel>();

            foreach (var row in rows.Where(r => r.IsSuspicious))
            {
                var key = $"{row.Pid}:{row.AlertText}";
                if (_notifiedKeys.Contains(key))
                    continue;

                _notifiedKeys.Add(key);
                newAlerts.Add(row);
            }

            foreach (var row in newAlerts)
            {
                var message = BuildMessage(row);

                if (_config.EnableTelegram)
                    await _telegram.SendAsync(_config, message);

                if (_config.EnableEmail)
                    await _email.SendAsync(_config, $"[CANH BAO] Tien trinh bat thuong - PID {row.Pid}", message);
            }
        }

        /// <summary>Dọn key cũ của tiến trình đã kết thúc, tránh HashSet phình to mãi theo thời gian.</summary>
        public void ForgetDeadProcesses(IEnumerable<int> alivePids)
        {
            var aliveSet = new HashSet<int>(alivePids);
            _notifiedKeys.RemoveWhere(key =>
                int.TryParse(key.Split(':')[0], out var pid) && !aliveSet.Contains(pid));
        }

        private string BuildMessage(ProcessRowViewModel row)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CANH BAO TIEN TRINH BAT THUONG");
            sb.AppendLine($"PID: {row.Pid}");
            sb.AppendLine($"Ten: {row.Name}");
            sb.AppendLine($"User: {row.UserName}");
            sb.AppendLine($"Duong dan: {row.ExePath}");
            sb.AppendLine($"Chi tiet: {row.AlertText}");
            sb.AppendLine($"Thoi gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            return sb.ToString();
        }
    }
}