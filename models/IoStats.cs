using System;

namespace monitorProcess.Models
{
    /// <summary>
    /// Thống kê I/O đĩa của 1 tiến trình, đọc từ /proc/[pid]/io.
    /// Tương ứng chức năng 8: Giám sát I/O đĩa cứng.
    /// Read/WriteBytesTotal là số lũy kế từ lúc tiến trình khởi chạy (rchar/wchar),
    /// còn Read/WriteSpeedBps là tốc độ tức thời, tính bằng cách lấy delta giữa
    /// 2 lần lấy mẫu liên tiếp rồi chia cho khoảng thời gian trôi qua.
    /// </summary>
    public class IoStats
    {
        public int Pid { get; set; }

        /// <summary>Tổng số byte đã đọc lũy kế (rchar trong /proc/[pid]/io).</summary>
        public long ReadBytesTotal { get; set; }

        /// <summary>Tổng số byte đã ghi lũy kế (wchar trong /proc/[pid]/io).</summary>
        public long WriteBytesTotal { get; set; }

        /// <summary>Tốc độ đọc tức thời, đơn vị byte/giây (tính từ delta 2 lần lấy mẫu).</summary>
        public double ReadSpeedBps { get; set; }

        /// <summary>Tốc độ ghi tức thời, đơn vị byte/giây (tính từ delta 2 lần lấy mẫu).</summary>
        public double WriteSpeedBps { get; set; }

        public DateTime SampledAt { get; set; } = DateTime.Now;
    }
}