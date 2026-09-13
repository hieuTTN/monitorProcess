namespace monitorProcess.Models
{
    /// <summary>
    /// Cấu hình ngưỡng cảnh báo CPU/RAM đột biến - dùng để phát hiện hành vi
    /// bất thường như CryptoMiner (tiêu tốn CPU cao bất thường).
    /// Tương ứng chức năng Detection: "Tiến trình tiêu tốn CPU/RAM đột biến".
    /// </summary>
    public class ThresholdConfig
    {
        public bool EnableCpuThreshold { get; set; }
        public double CpuThresholdPercent { get; set; } = 80;

        public bool EnableRamThreshold { get; set; }
        public double RamThresholdPercent { get; set; } = 50;
    }
}