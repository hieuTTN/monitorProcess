using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace monitorProcess.Services
{
    /// <summary>Ghi lại 1 lần "lẽ ra đã bị chặn" khi ExecGuard đang chạy ở Log-only Mode.</summary>
    public class ExecGuardSuggestion
    {
        public string ExePath { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int SeenCount { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }
    }

    /// <summary>
    /// Thu thập danh sách "ứng viên whitelist" khi ExecGuardService chạy ở chế
    /// độ Log-only (chỉ ghi lại, KHÔNG chặn thật). Gộp trùng theo ExePath, đếm
    /// số lần gặp - giúp bạn nhìn 1 lần duy nhất để quyết định thêm hàng loạt
    /// vào whitelist, thay vì phải chạy-thử-xem lỗi-thêm-chạy lại từng cái 1.
    /// </summary>
    public class ExecGuardSuggestionService
    {
        private readonly string _dataFilePath;
        private readonly object _lock = new();
        private Dictionary<string, ExecGuardSuggestion> _suggestions = new();

        public ExecGuardSuggestionService(string dataFilePath)
        {
            _dataFilePath = dataFilePath;
            Load();
        }

        public void Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_dataFilePath))
                {
                    _suggestions = new Dictionary<string, ExecGuardSuggestion>();
                    return;
                }

                var json = File.ReadAllText(_dataFilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var list = JsonSerializer.Deserialize<List<ExecGuardSuggestion>>(json, options)
                           ?? new List<ExecGuardSuggestion>();
                _suggestions = list.ToDictionary(x => x.ExePath, x => x);
            }
        }

        private void Save()
        {
            var directory = Path.GetDirectoryName(_dataFilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_dataFilePath, JsonSerializer.Serialize(_suggestions.Values.ToList(), options));
        }

        /// <summary>
        /// Ghi nhận 1 lần "lẽ ra bị chặn". Gọi từ ExecGuardService khi đang ở
        /// Log-only Mode. Thread-safe vì hàm này được gọi từ nhiều Task chạy
        /// song song (mỗi sự kiện fanotify xử lý trên 1 Task riêng).
        /// </summary>
        public void RecordSeen(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return;

            lock (_lock)
            {
                var now = DateTime.Now;
                var processName = Path.GetFileName(exePath);

                if (_suggestions.TryGetValue(exePath, out var existing))
                {
                    existing.SeenCount++;
                    existing.LastSeenAt = now;
                }
                else
                {
                    _suggestions[exePath] = new ExecGuardSuggestion
                    {
                        ExePath = exePath,
                        ProcessName = processName,
                        SeenCount = 1,
                        FirstSeenAt = now,
                        LastSeenAt = now
                    };
                }

                Save();
            }
        }

        public List<ExecGuardSuggestion> GetAll()
        {
            lock (_lock)
            {
                return _suggestions.Values.OrderByDescending(x => x.SeenCount).ToList();
            }
        }

        public void Remove(string exePath)
        {
            lock (_lock)
            {
                _suggestions.Remove(exePath);
                Save();
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _suggestions.Clear();
                Save();
            }
        }
    }
}