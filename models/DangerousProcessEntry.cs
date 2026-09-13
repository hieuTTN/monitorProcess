using System;

namespace monitorProcess.Models
{
    /// <summary>
    /// Một mục trong CSDL tiến trình nguy hiểm đã biết (blacklist), được tải về
    /// (ví dụ từ file JSON/CSV nội bộ, hoặc từ nguồn threat-intel).
    /// Nếu tiến trình đang chạy có tên hoặc hash trùng với 1 mục trong danh sách
    /// này, hệ thống sẽ cảnh báo (AlertType.MatchDangerousDb).
    /// </summary>
    public class DangerousProcessEntry
    {
        /// <summary>Tên tiến trình nguy hiểm đã biết (ví dụ: "xmrig", "mimikatz").</summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>
        /// Hash SHA-256 của mẫu nguy hiểm đã biết (nếu có). Dùng để so khớp chính
        /// xác hơn là chỉ so tên, vì tên tiến trình dễ bị đổi/giả mạo.
        /// </summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>Phân loại: Malware, CryptoMiner, Rootkit, Backdoor, Khác...</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Mức độ nguy hiểm mặc định khi phát hiện trùng khớp.</summary>
        public AlertSeverity Severity { get; set; } = AlertSeverity.Critical;

        /// <summary>Mô tả thêm về mối nguy hiểm này.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Nguồn dữ liệu (ví dụ: tên file CSDL, hoặc URL nếu tải từ internet).</summary>
        public string Source { get; set; } = string.Empty;
    }
}