using System;

namespace monitorProcess.Models
{
    /// <summary>
    /// Một bản ghi cảnh báo khi phát hiện tiến trình bất thường.
    /// Tương ứng chức năng 4 (path nguy hiểm), 6 (lưu nhật ký), và các cảnh báo
    /// liên quan tới whitelist/blacklist tiến trình.
    /// Đây cũng là đơn vị dữ liệu được gửi qua Email/Telegram (chức năng 10)
    /// và xuất ra báo cáo CSV/PDF (chức năng 7).
    /// </summary>
    public class AlertLog
    {
        /// <summary>Id định danh duy nhất cho bản ghi log (dùng Guid cho đơn giản).</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public int Pid { get; set; }

        public string ProcessName { get; set; } = string.Empty;

        public string ExePath { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        /// <summary>Loại cảnh báo (path nguy hiểm, sai hash, không trong whitelist...).</summary>
        public AlertType Type { get; set; }

        public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

        /// <summary>Nội dung mô tả chi tiết, hiển thị cho người dùng / gửi qua Telegram-Email.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Hành động đã thực hiện đối với tiến trình này (nếu có).</summary>
        public ActionTaken Action { get; set; } = ActionTaken.None;

        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] ({Severity}) {Type} - PID {Pid} ({ProcessName}): {Message}";
        }
    }
}