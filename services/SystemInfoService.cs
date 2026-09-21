using System;
using System.IO;
using System.Linq;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Thu thập thông tin tổng quan của TOÀN HỆ THỐNG (không phải theo từng
    /// tiến trình) - phục vụ khối "System Health Overview" trên Dashboard.
    /// Không cần quyền sudo vì các file /proc này đều world-readable.
    /// </summary>
    public class SystemInfoService
    {
        private class CpuSample { public long Idle; public long Total; }
        private class DiskSample { public long ReadSectors; public long WriteSectors; public DateTime Timestamp; }
        private class NetSample { public long RxBytes; public long TxBytes; public DateTime Timestamp; }

        private CpuSample? _prevCpu;
        private DiskSample? _prevDisk;
        private NetSample? _prevNet;

        public string GetHostname()
        {
            try
            {
                const string path = "/proc/sys/kernel/hostname";
                if (File.Exists(path))
                    return File.ReadAllText(path).Trim();
            }
            catch { }
            return Environment.MachineName;
        }

        public string GetOsVersion()
        {
            const string path = "/etc/os-release";
            if (!File.Exists(path)) return "Không xác định";

            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("PRETTY_NAME="))
                    return line.Substring("PRETTY_NAME=".Length).Trim('"');
            }
            return "Không xác định";
        }

        public string GetKernelVersion()
        {
            const string path = "/proc/sys/kernel/osrelease";
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "Không xác định";
        }

        public string GetUptime()
        {
            const string path = "/proc/uptime";
            if (!File.Exists(path)) return "-";

            var content = File.ReadAllText(path);
            var seconds = double.Parse(content.Split(' ')[0]);
            var ts = TimeSpan.FromSeconds(seconds);

            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays} ngày {ts.Hours} giờ {ts.Minutes} phút";
            if (ts.TotalHours >= 1)
                return $"{ts.Hours} giờ {ts.Minutes} phút";
            return $"{ts.Minutes} phút";
        }

        /// <summary>Đo tất cả chỉ số động (CPU/RAM/Disk/Network) tại thời điểm hiện tại.</summary>
        public SystemSnapshot Measure()
        {
            var snapshot = new SystemSnapshot { SampledAt = DateTime.Now };

            MeasureCpu(snapshot);
            MeasureMemory(snapshot);
            MeasureDisk(snapshot);
            MeasureNetwork(snapshot);

            return snapshot;
        }

        private void MeasureCpu(SystemSnapshot snapshot)
        {
            const string path = "/proc/stat";
            if (!File.Exists(path)) return;

            // Dòng đầu tiên "cpu  ..." là tổng cộng dồn của TẤT CẢ lõi CPU.
            var firstLine = File.ReadLines(path).First();
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Select(long.Parse).ToArray();

            var idle = parts[3] + (parts.Length > 4 ? parts[4] : 0); // idle + iowait
            var total = parts.Sum();

            if (_prevCpu != null)
            {
                var deltaTotal = total - _prevCpu.Total;
                var deltaIdle = idle - _prevCpu.Idle;

                if (deltaTotal > 0)
                    snapshot.CpuPercent = Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100.0, 1);
            }

            _prevCpu = new CpuSample { Idle = idle, Total = total };
        }

        private void MeasureMemory(SystemSnapshot snapshot)
        {
            const string path = "/proc/meminfo";
            if (!File.Exists(path)) return;

            long memTotal = 0, memAvailable = 0;

            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("MemTotal:"))
                    memTotal = ParseKb(line);
                else if (line.StartsWith("MemAvailable:"))
                    memAvailable = ParseKb(line);
            }

            var usedKb = memTotal - memAvailable;

            snapshot.MemoryTotalMb = Math.Round(memTotal / 1024.0, 0);
            snapshot.MemoryUsedMb = Math.Round(usedKb / 1024.0, 0);
            snapshot.MemoryPercent = memTotal > 0 ? Math.Round(usedKb / (double)memTotal * 100.0, 1) : 0;
        }

        private void MeasureDisk(SystemSnapshot snapshot)
        {
            const string path = "/proc/diskstats";
            if (!File.Exists(path)) return;

            long totalReadSectors = 0, totalWriteSectors = 0;

            foreach (var line in File.ReadLines(path))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 10) continue;

                var deviceName = fields[2];

                // Chỉ cộng ổ đĩa vật lý gốc, bỏ qua loop/ram device và partition
                // con (vd "sda1") để tránh cộng dồn trùng I/O.
                if (deviceName.StartsWith("loop") || deviceName.StartsWith("ram") ||
                    IsPartition(deviceName))
                    continue;

                totalReadSectors += long.Parse(fields[5]);
                totalWriteSectors += long.Parse(fields[9]);
            }

            var now = DateTime.Now;

            if (_prevDisk != null)
            {
                var elapsed = (now - _prevDisk.Timestamp).TotalSeconds;
                if (elapsed > 0)
                {
                    var deltaRead = (totalReadSectors - _prevDisk.ReadSectors) * 512.0;
                    var deltaWrite = (totalWriteSectors - _prevDisk.WriteSectors) * 512.0;

                    snapshot.DiskReadBps = deltaRead > 0 ? Math.Round(deltaRead / elapsed, 0) : 0;
                    snapshot.DiskWriteBps = deltaWrite > 0 ? Math.Round(deltaWrite / elapsed, 0) : 0;
                }
            }

            _prevDisk = new DiskSample
            {
                ReadSectors = totalReadSectors,
                WriteSectors = totalWriteSectors,
                Timestamp = now
            };
        }

        private void MeasureNetwork(SystemSnapshot snapshot)
        {
            const string path = "/proc/net/dev";
            if (!File.Exists(path)) return;

            long totalRx = 0, totalTx = 0;

            foreach (var line in File.ReadLines(path).Skip(2)) // 2 dòng đầu là header
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0) continue;

                var ifaceName = line.Substring(0, colonIndex).Trim();
                if (ifaceName == "lo") continue; // Bỏ qua loopback

                var fields = line.Substring(colonIndex + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9) continue;

                totalRx += long.Parse(fields[0]); // bytes received
                totalTx += long.Parse(fields[8]); // bytes transmitted
            }

            var now = DateTime.Now;

            if (_prevNet != null)
            {
                var elapsed = (now - _prevNet.Timestamp).TotalSeconds;
                if (elapsed > 0)
                {
                    var deltaRx = totalRx - _prevNet.RxBytes;
                    var deltaTx = totalTx - _prevNet.TxBytes;

                    snapshot.NetRxBps = deltaRx > 0 ? Math.Round(deltaRx / elapsed, 0) : 0;
                    snapshot.NetTxBps = deltaTx > 0 ? Math.Round(deltaTx / elapsed, 0) : 0;
                }
            }

            _prevNet = new NetSample { RxBytes = totalRx, TxBytes = totalTx, Timestamp = now };
        }

        private static long ParseKb(string line)
        {
            var value = line.Substring(line.IndexOf(':') + 1).Trim();
            var numPart = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return long.TryParse(numPart, out var kb) ? kb : 0;
        }

        /// <summary>
        /// Heuristic đơn giản để nhận diện partition (vd "sda1") thay vì ổ đĩa
        /// vật lý gốc (vd "sda"). Không xử lý hoàn hảo mọi trường hợp (ví dụ
        /// "mmcblk0" của thẻ SD sẽ bị nhận nhầm là partition do kết thúc bằng
        /// số) - chấp nhận được cho mục đích giám sát tổng quan.
        /// </summary>
        private static bool IsPartition(string deviceName)
        {
            if (deviceName.Length == 0 || !char.IsDigit(deviceName[^1]))
                return false;

            if (deviceName.StartsWith("nvme"))
                return deviceName.Contains('p'); // "nvme0n1p1" có 'p' -> partition

            return true; // "sda1", "vda1"... kết thúc bằng số -> coi là partition
        }


        /// <summary>Đọc tên đầy đủ của CPU (vd "Intel(R) Core(TM) i5-10210U CPU @ 1.60GHz") từ /proc/cpuinfo.</summary>
        public string GetCpuModelName()
        {
            const string path = "/proc/cpuinfo";
            if (!File.Exists(path)) return "Không xác định";

            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("model name"))
                {
                    var idx = line.IndexOf(':');
                    return idx >= 0 ? line.Substring(idx + 1).Trim() : "Không xác định";
                }
            }
            return "Không xác định";
        }

        /// <summary>Số nhân vật lý (cores) và số luồng logic (logical processors) của CPU.</summary>
        public (int cores, int logicalProcessors) GetCpuCoreInfo()
        {
            const string path = "/proc/cpuinfo";
            if (!File.Exists(path)) return (0, 0);

            var logical = 0;
            var cores = 0;

            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("processor"))
                {
                    logical++;
                }
                else if (line.StartsWith("cpu cores") && cores == 0)
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0 && int.TryParse(line.Substring(idx + 1).Trim(), out var c))
                        cores = c;
                }
            }

            // Một số máy ảo không có field "cpu cores" -> fallback dùng luôn số luồng logic.
            if (cores == 0) cores = logical;

            return (cores, logical);
        }

        /// <summary>Đếm nhanh tổng số tiến trình đang chạy (số thư mục PID trong /proc).</summary>
        public int GetProcessCount()
        {
            try
            {
                return Directory.EnumerateDirectories("/proc")
                    .Count(d => int.TryParse(Path.GetFileName(d), out _));
            }
            catch
            {
                return 0;
            }
        }
        
    }
}