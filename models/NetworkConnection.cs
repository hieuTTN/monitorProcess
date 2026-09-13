using System;

namespace monitorProcess.Models
{
    /// <summary>
    /// Thông tin 1 kết nối/socket của tiến trình, đối chiếu từ /proc/net/tcp,
    /// /proc/net/udp và /proc/[pid]/fd/.
    /// Tương ứng chức năng 9: Giám sát mạng của tiến trình (Network Binding).
    /// </summary>
    public class NetworkConnection
    {
        /// <summary>PID của tiến trình sở hữu socket này (map ngược qua /proc/[pid]/fd/).</summary>
        public int Pid { get; set; }

        /// <summary>Giao thức: TCP hoặc UDP.</summary>
        public string Protocol { get; set; } = string.Empty;

        public string LocalAddress { get; set; } = string.Empty;

        public int LocalPort { get; set; }

        public string RemoteAddress { get; set; } = string.Empty;

        public int RemotePort { get; set; }

        /// <summary>Trạng thái socket (LISTEN, ESTABLISHED, TIME_WAIT...), chỉ có ý nghĩa với TCP.</summary>
        public string State { get; set; } = string.Empty;

        public DateTime SampledAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"{Protocol} {LocalAddress}:{LocalPort} -> {RemoteAddress}:{RemotePort} [{State}]";
        }
    }
}