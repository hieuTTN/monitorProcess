using System;

namespace monitorProcess.Models
{
    /// <summary>
    /// Thông tin cơ bản của 1 tiến trình, thu thập từ /proc/[pid]/status,
    /// /proc/[pid]/cmdline, /proc/[pid]/exe.
    /// Tương ứng chức năng 1: Liệt kê danh sách tiến trình.
    /// </summary>
    public class ProcessInfo
    {
        /// <summary>Mã tiến trình (Process ID).</summary>
        public int Pid { get; set; }

        /// <summary>Mã tiến trình cha (Parent Process ID) - đọc từ /proc/[pid]/status.</summary>
        public int Ppid { get; set; }

        /// <summary>Tên tiến trình (đọc từ /proc/[pid]/comm).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>UID của user sở hữu tiến trình.</summary>
        public int Uid { get; set; }

        /// <summary>Tên user sở hữu (map từ UID sang username, ví dụ qua /etc/passwd).</summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>Câu lệnh đầy đủ khi khởi chạy tiến trình (đọc từ /proc/[pid]/cmdline).</summary>
        public string CmdLine { get; set; } = string.Empty;

        /// <summary>
        /// Đường dẫn thật của file thực thi (đọc từ symlink /proc/[pid]/exe).
        /// Có thể rỗng nếu tiến trình đã kết thúc hoặc không có quyền đọc.
        /// </summary>
        public string ExePath { get; set; } = string.Empty;

        /// <summary>Trạng thái hiện tại của tiến trình.</summary>
        public ProcessState State { get; set; } = ProcessState.Unknown;

        /// <summary>Thời điểm tiến trình được khởi tạo (ước tính từ /proc/[pid]/stat).</summary>
        public DateTime StartTime { get; set; }

        /// <summary>Thời điểm bản ghi này được thu thập (dùng để biết dữ liệu "mới" tới đâu).</summary>
        public DateTime CollectedAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"[{Pid}] {Name} (User: {UserName}, State: {State}) - {ExePath}";
        }
    }
}