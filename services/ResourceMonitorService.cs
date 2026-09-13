using System;
using System.Collections.Generic;
using System.IO;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Giám sát CPU% và RAM của từng tiến trình.
    /// Tương ứng chức năng 2: Giám sát tài nguyên CPU/RAM.
    ///
    /// CPU% không có sẵn trong /proc - phải tự tính bằng cách lấy 2 mẫu thời
    /// gian CPU (utime + stime, đơn vị "clock tick") cách nhau 1 khoảng thời
    /// gian, rồi suy ra % dựa trên: (delta CPU time / delta thời gian thực) * 100.
    /// Vì vậy service này có trạng thái (lưu mẫu trước đó) - cần gọi Measure()
    /// định kỳ (ví dụ mỗi 3 giây cùng chu kỳ LoadProcesses), lần gọi đầu tiên
    /// cho 1 PID sẽ luôn trả về CPU% = 0 vì chưa có mẫu để so sánh.
    /// </summary>
    public class ResourceMonitorService
    {
        // Số tick đồng hồ hệ thống mỗi giây (USER_HZ). Hầu hết Linux/Ubuntu
        // trên x86_64 dùng mặc định 100 theo glibc.
        private const long ClockTicksPerSecond = 100;

        private class Sample
        {
            public long TotalTicks;
            public DateTime Timestamp;
        }

        private readonly Dictionary<int, Sample> _previousSamples = new();
        private long _totalMemoryKb = -1;
        private DateTime _totalMemoryLoadedAt = DateTime.MinValue;

        /// <summary>Đo CPU%/RAM hiện tại của 1 tiến trình. Trả về null nếu tiến trình không còn tồn tại.</summary>
        public ResourceUsage? Measure(int pid)
        {
            var statPath = $"/proc/{pid}/stat";
            var statusPath = $"/proc/{pid}/status";

            if (!File.Exists(statPath))
                return null;

            long utime, stime;
            try
            {
                var statContent = File.ReadAllText(statPath);

                // Tên tiến trình nằm trong dấu ngoặc đơn và có thể chứa khoảng
                // trắng (ví dụ "(my process)"), nên phải tìm dấu ")" CUỐI CÙNG
                // rồi mới tách các trường phía sau theo khoảng trắng.
                var closeParen = statContent.LastIndexOf(')');
                var afterName = statContent.Substring(closeParen + 2);
                var fields = afterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Trường gốc thứ 14 (utime) và 15 (stime) trong /proc/pid/stat,
                // sau khi cắt bỏ pid+comm+")" thì tương ứng index 11 và 12.
                utime = long.Parse(fields[11]);
                stime = long.Parse(fields[12]);
            }
            catch
            {
                return null;
            }

            var totalTicks = utime + stime;
            var now = DateTime.Now;

            double cpuPercent = 0;
            if (_previousSamples.TryGetValue(pid, out var prev))
            {
                var elapsedSeconds = (now - prev.Timestamp).TotalSeconds;
                var deltaTicks = totalTicks - prev.TotalTicks;

                if (elapsedSeconds > 0 && deltaTicks >= 0)
                {
                    var deltaCpuSeconds = deltaTicks / (double)ClockTicksPerSecond;
                    cpuPercent = deltaCpuSeconds / elapsedSeconds * 100.0;
                }
            }

            _previousSamples[pid] = new Sample { TotalTicks = totalTicks, Timestamp = now };

            var memoryKb = ReadVmRss(statusPath);
            var totalMemKb = GetTotalMemoryKb();
            var memoryPercent = totalMemKb > 0 ? memoryKb / (double)totalMemKb * 100.0 : 0;

            return new ResourceUsage
            {
                Pid = pid,
                CpuPercent = Math.Round(cpuPercent, 1),
                MemoryKb = memoryKb,
                MemoryPercent = Math.Round(memoryPercent, 1),
                SampledAt = now
            };
        }

        /// <summary>
        /// Dọn các mẫu cũ của tiến trình đã kết thúc, tránh Dictionary phình to
        /// mãi theo thời gian khi tiến trình liên tục tạo mới/kết thúc.
        /// Gọi hàm này sau mỗi lần quét toàn bộ danh sách tiến trình.
        /// </summary>
        public void ForgetDeadProcesses(IEnumerable<int> alivePids)
        {
            var aliveSet = new HashSet<int>(alivePids);
            var deadKeys = new List<int>();

            foreach (var pid in _previousSamples.Keys)
            {
                if (!aliveSet.Contains(pid))
                    deadKeys.Add(pid);
            }

            foreach (var pid in deadKeys)
                _previousSamples.Remove(pid);
        }

        private long ReadVmRss(string statusPath)
        {
            if (!File.Exists(statusPath))
                return 0;

            foreach (var line in File.ReadLines(statusPath))
            {
                if (line.StartsWith("VmRSS:"))
                {
                    // Dạng: "VmRSS:\t  1234 kB"
                    var value = line.Substring("VmRSS:".Length).Trim();
                    var numPart = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                    if (long.TryParse(numPart, out var kb))
                        return kb;
                }
            }

            return 0;
        }

        private long GetTotalMemoryKb()
        {
            // Cache lại vì tổng RAM hệ thống gần như không đổi trong lúc chạy.
            if (_totalMemoryKb > 0 && DateTime.Now - _totalMemoryLoadedAt < TimeSpan.FromMinutes(5))
                return _totalMemoryKb;

            const string meminfoPath = "/proc/meminfo";
            if (!File.Exists(meminfoPath))
                return _totalMemoryKb > 0 ? _totalMemoryKb : 1;

            foreach (var line in File.ReadLines(meminfoPath))
            {
                if (line.StartsWith("MemTotal:"))
                {
                    var value = line.Substring("MemTotal:".Length).Trim();
                    var numPart = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                    if (long.TryParse(numPart, out var kb))
                    {
                        _totalMemoryKb = kb;
                        _totalMemoryLoadedAt = DateTime.Now;
                        return kb;
                    }
                }
            }

            return _totalMemoryKb > 0 ? _totalMemoryKb : 1;
        }
    }
}