namespace monitorProcess.Models
{
    /// <summary>
    /// 1 mục trong CSDL hash đáng tin cậy - dùng cho cả 2 tiêu chí "Không có
    /// hash trong database" và "File thực thi thay đổi" trong Risk Scoring.
    /// </summary>
    public class KnownHashEntry
    {
        public string ExePath { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}