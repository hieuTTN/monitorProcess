using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Quản lý CSDL các tiến trình nguy hiểm đã biết (blacklist / signature-based
    /// detection). Nếu tiến trình đang chạy trùng tên hoặc trùng hash SHA-256 với
    /// 1 mục trong CSDL này thì coi là phát hiện được mối nguy hiểm cụ thể.
    /// </summary>
    public class DangerousProcessDbService
    {
        private readonly string _dataFilePath;
        private readonly HashService _hashService;
        private List<DangerousProcessEntry> _entries = new List<DangerousProcessEntry>();

        public DangerousProcessDbService(string dataFilePath, HashService hashService)
        {
            _dataFilePath = dataFilePath;
            _hashService = hashService;
            Load();
        }

        /// <summary>Nạp (hoặc nạp lại / "tải về") CSDL tiến trình nguy hiểm từ file JSON.</summary>
        public void Load()
        {
            if (!File.Exists(_dataFilePath))
            {
                _entries = new List<DangerousProcessEntry>();
                return;
            }

            var json = File.ReadAllText(_dataFilePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            _entries = JsonSerializer.Deserialize<List<DangerousProcessEntry>>(json, options)
                       ?? new List<DangerousProcessEntry>();
        }

        public IReadOnlyList<DangerousProcessEntry> GetAll() => _entries;

        /// <summary>
        /// Kiểm tra 1 tiến trình so với CSDL nguy hiểm. Ưu tiên so khớp theo hash
        /// (chính xác hơn) nếu mục CSDL có khai báo hash, ngược lại so theo tên.
        /// Trả về AlertLog nếu trùng khớp, hoặc null nếu không phát hiện gì.
        /// </summary>
        public AlertLog? Check(ProcessInfo process)
        {
            string? exeHash = null;

            foreach (var entry in _entries)
            {
                var nameMatches = string.Equals(entry.ProcessName, process.Name, StringComparison.OrdinalIgnoreCase);

                var hashMatches = false;
                if (!string.IsNullOrEmpty(entry.Sha256))
                {
                    // Chỉ tính hash 1 lần (tốn I/O) rồi tái sử dụng cho các lần so sánh tiếp theo.
                    exeHash ??= _hashService.ComputeSha256(process.ExePath);
                    hashMatches = exeHash != null &&
                                  string.Equals(entry.Sha256, exeHash, StringComparison.OrdinalIgnoreCase);
                }

                if (nameMatches || hashMatches)
                {
                    return new AlertLog
                    {
                        Pid = process.Pid,
                        ProcessName = process.Name,
                        ExePath = process.ExePath,
                        UserName = process.UserName,
                        Type = AlertType.MatchDangerousDb,
                        Severity = entry.Severity,
                        Message = $"Tiến trình '{process.Name}' (PID {process.Pid}) trùng khớp với " +
                                  $"CSDL nguy hiểm - Loại: {entry.Category}. {entry.Description}"
                    };
                }
            }

            return null;
        }

        public List<AlertLog> CheckAll(IEnumerable<ProcessInfo> processes)
        {
            var alerts = new List<AlertLog>();
            foreach (var p in processes)
            {
                var alert = Check(p);
                if (alert != null)
                    alerts.Add(alert);
            }
            return alerts;
        }
    }
}