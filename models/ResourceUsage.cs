using System;

namespace monitorProcess.Models
{
    /// <summary>
    /// Mức sử dụng tài nguyên CPU/RAM của 1 tiến trình tại 1 thời điểm lấy mẫu.
    /// Tương ứng chức năng 2: Giám sát tài nguyên CPU/RAM.
    /// CPU% được tính bằng cách lấy delta thời gian CPU (từ /proc/[pid]/stat)
    /// giữa 2 lần lấy mẫu, chia cho delta thời gian thực tế trôi qua.
    /// </summary>
    public class ResourceUsage
    {
        public int Pid { get; set; }

        /// <summary>Phần trăm CPU đang sử dụng (0-100, có thể vượt 100 nếu multi-core).</summary>
        public double CpuPercent { get; set; }

        /// <summary>Bộ nhớ RAM thực tế đang chiếm dụng, tính theo KB (đọc từ VmRSS trong /proc/[pid]/status).</summary>
        public long MemoryKb { get; set; }

        /// <summary>Bộ nhớ RAM đang chiếm dụng, tính theo % so với tổng RAM hệ thống.</summary>
        public double MemoryPercent { get; set; }

        /// <summary>Thời điểm lấy mẫu.</summary>
        public DateTime SampledAt { get; set; } = DateTime.Now;
    }
}