using System;

namespace monitorProcess.Models
{
    /// <summary>
    /// Ảnh chụp trạng thái tổng quan của toàn hệ thống tại 1 thời điểm - dùng
    /// cho khối "Tổng quan Hệ thống" trên trang Dashboard.
    /// </summary>
    public class SystemSnapshot
    {
        public double CpuPercent { get; set; }

        public double MemoryUsedMb { get; set; }
        public double MemoryTotalMb { get; set; }
        public double MemoryPercent { get; set; }

        public double DiskReadBps { get; set; }
        public double DiskWriteBps { get; set; }

        public double NetRxBps { get; set; }
        public double NetTxBps { get; set; }

        public DateTime SampledAt { get; set; } = DateTime.Now;
    }
}