using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Quản lý danh sách "chỉ cho phép chạy 1 số tiến trình nhất định" - phù hợp
    /// với môi trường cơ quan nhà nước chỉ dùng 1 tập phần mềm cố định (mô hình
    /// bảo mật "default deny": mặc định chặn, chỉ cho phép thứ nằm trong danh sách).
    ///
    /// Chỉ so khớp theo 2 tiêu chí:
    ///  - ProcessName: so với TÊN THẬT của file thực thi (basename), không phải
    ///    tên bị kernel cắt cụt 15 ký tự.
    ///  - ExpectedSha256 (TUỲ CHỌN): nếu để rỗng thì KHÔNG kiểm tra hash, chỉ
    ///    cần đúng tên là được phép. Nếu có ghi hash, file thực thi PHẢI khớp
    ///    đúng hash đó mới được phép (chặn trường hợp bị thay file giả mạo
    ///    nhưng đặt cùng tên).
    /// </summary>
    public class WhitelistService
    {
        private readonly string _dataFilePath;
        private readonly HashService _hashService;
        private List<WhitelistEntry> _entries = new List<WhitelistEntry>();

        public WhitelistService(string dataFilePath, HashService hashService)
        {
            _dataFilePath = dataFilePath;
            _hashService = hashService;
            Load();
        }

        /// <summary>Nạp (hoặc nạp lại) danh sách whitelist từ file JSON trên đĩa.</summary>
        public void Load()
        {
            if (!File.Exists(_dataFilePath))
            {
                _entries = new List<WhitelistEntry>();
                return;
            }

            var json = File.ReadAllText(_dataFilePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _entries = JsonSerializer.Deserialize<List<WhitelistEntry>>(json, options)
                       ?? new List<WhitelistEntry>();
        }

        public IReadOnlyList<WhitelistEntry> GetAll() => _entries;

        /// <summary>
        /// Kiểm tra CHỈ dựa trên đường dẫn file thực thi - dùng cho ExecGuardService,
        /// vì tại thời điểm chặn (trước khi exec xong), ta chỉ có đường dẫn file
        /// sắp chạy chứ CHƯA có đầy đủ ProcessInfo.
        /// Không đọc được đường dẫn -> coi là KHÔNG được phép (an toàn là trên hết).
        /// </summary>
        public bool IsExecutablePathAllowed(string exePath)
        {
            if (_entries.Count == 0)
                return true;

            if (string.IsNullOrEmpty(exePath))
                return false;

            var baseName = Path.GetFileName(exePath);

            var matchingEntries = _entries.Where(e =>
                string.Equals(e.ProcessName, baseName, StringComparison.OrdinalIgnoreCase)
                // Tên trong whitelist dài hơn 15 ký tự sẽ bị kernel cắt cụt khi báo
                // về qua /proc/[pid]/status -> phòng hờ so khớp thêm phần đã cắt.
                || (e.ProcessName.Length > 15
                    && string.Equals(e.ProcessName.Substring(0, 15), baseName, StringComparison.OrdinalIgnoreCase)));

            foreach (var e in matchingEntries)
            {
                if (string.IsNullOrEmpty(e.ExpectedSha256))
                    return true; // Không cấu hình hash cho entry này -> chỉ cần khớp tên là đủ.

                var actualHash = _hashService.ComputeSha256(exePath);
                if (actualHash != null &&
                    string.Equals(actualHash, e.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                // Tên khớp nhưng hash sai (hoặc không tính được hash) -> thử entry khác,
                // không return false ngay để không bỏ lỡ 1 entry trùng tên khác hash đúng.
            }

            return false;
        }

        /// <summary>
        /// Kiểm tra 1 tiến trình có nằm trong whitelist hay không (dùng cho phần
        /// hiển thị/cảnh báo trong ProcessListView, dựa trên ProcessInfo đầy đủ).
        /// Nếu whitelist trống thì coi như chưa cấu hình, KHÔNG chặn gì.
        /// </summary>
        public bool IsAllowed(ProcessInfo process)
        {
            if (_entries.Count == 0)
                return true;

            return IsExecutablePathAllowed(process.ExePath) ||
                   // process.ExePath có thể rỗng (không đọc được /proc/[pid]/exe, ví dụ
                   // kernel thread) -> fallback so theo tên kernel báo về (có thể bị cắt).
                   _entries.Any(e => string.Equals(e.ProcessName, process.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Kiểm tra 1 tiến trình, trả về AlertLog nếu KHÔNG nằm trong whitelist,
        /// hoặc null nếu tiến trình được phép chạy.
        /// </summary>
        public AlertLog? Check(ProcessInfo process)
        {
            if (IsAllowed(process))
                return null;

            return new AlertLog
            {
                Pid = process.Pid,
                ProcessName = process.Name,
                ExePath = process.ExePath,
                UserName = process.UserName,
                Type = AlertType.NotInWhitelist,
                Severity = AlertSeverity.Warning,
                Message = $"Tiến trình '{process.Name}' (PID {process.Pid}) không nằm trong " +
                          $"danh sách phần mềm được phép sử dụng."
            };
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