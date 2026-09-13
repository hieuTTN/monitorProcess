using System.Collections.Generic;

namespace monitorProcess.Models
{
    /// <summary>
    /// Gộp toàn bộ thông tin của 1 tiến trình tại 1 thời điểm quét: thông tin cơ
    /// bản + tài nguyên + I/O + các kết nối mạng. Đây là đơn vị dữ liệu chính mà
    /// tầng Dashboard/CLI sẽ hiển thị, và cũng là input để tầng Detection phân
    /// tích tìm bất thường.
    /// </summary>
    public class ProcessSnapshot
    {
        public ProcessInfo Info { get; set; } = new ProcessInfo();

        public ResourceUsage Resource { get; set; } = new ResourceUsage();

        public IoStats Io { get; set; } = new IoStats();

        public List<NetworkConnection> Connections { get; set; } = new List<NetworkConnection>();

        /// <summary>
        /// Các cảnh báo phát sinh cho tiến trình này trong lần quét hiện tại
        /// (rỗng nếu tiến trình bình thường, không có gì bất thường).
        /// </summary>
        public List<AlertLog> Alerts { get; set; } = new List<AlertLog>();

        /// <summary>Tiến trình này có đang bị coi là bất thường hay không (tiện cho việc lọc/tô màu trên UI).</summary>
        public bool IsSuspicious => Alerts.Count > 0;
    }
}