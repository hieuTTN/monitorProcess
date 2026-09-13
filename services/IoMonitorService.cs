using System;
using System.Collections.Generic;
using System.IO;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Giám sát tốc độ đọc/ghi đĩa của từng tiến trình.
    /// Tương ứng chức năng 8: Giám sát I/O đĩa cứng (Read/Write speed).
    ///
    /// /proc/[pid]/io chỉ cho số byte lũy kế (rchar/wchar) từ lúc tiến trình
    /// khởi chạy, không có sẵn "tốc độ" - phải tự lấy mẫu 2 lần cách nhau 1
    /// khoảng thời gian rồi tính: (delta byte / delta thời gian) = byte/giây,
    /// cùng nguyên lý với ResourceMonitorService khi tính CPU%.
    ///
    /// Lưu ý quyền hạn: /proc/[pid]/io của tiến trình thuộc user KHÁC chỉ đọc
    /// được nếu chạy ứng dụng bằng sudo/root. Nếu không đủ quyền, Measure() sẽ
    /// trả về null cho tiến trình đó (không phải lỗi, là giới hạn quyền hệ thống).
    /// </summary>
    public class IoMonitorService
    {
        private class Sample
        {
            public long ReadBytesTotal;
            public long WriteBytesTotal;
            public DateTime Timestamp;
        }

        private readonly Dictionary<int, Sample> _previousSamples = new();

        public IoStats? Measure(int pid)
        {
            var ioPath = $"/proc/{pid}/io";
            if (!File.Exists(ioPath))
                return null;

            long readBytes = 0, writeBytes = 0;

            try
            {
                foreach (var line in File.ReadLines(ioPath))
                {
                    // rchar: tổng số byte tiến trình đã đọc (kể cả từ cache, chưa
                    // chắc đã chạm đĩa thật) - dùng làm số liệu "đọc" tổng quát,
                    // đơn giản và đủ dùng cho mục đích giám sát.
                    if (line.StartsWith("rchar:"))
                    {
                        readBytes = ParseValue(line, "rchar:");
                    }
                    else if (line.StartsWith("wchar:"))
                    {
                        writeBytes = ParseValue(line, "wchar:");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Không đủ quyền đọc I/O của tiến trình user khác -> coi như
                // không đo được, không phải lỗi chương trình.
                return null;
            }
            catch (IOException)
            {
                return null;
            }

            var now = DateTime.Now;
            double readSpeed = 0, writeSpeed = 0;

            if (_previousSamples.TryGetValue(pid, out var prev))
            {
                var elapsedSeconds = (now - prev.Timestamp).TotalSeconds;

                if (elapsedSeconds > 0)
                {
                    var deltaRead = readBytes - prev.ReadBytesTotal;
                    var deltaWrite = writeBytes - prev.WriteBytesTotal;

                    // Delta âm có thể xảy ra nếu bộ đếm bị reset (hiếm, ví dụ
                    // tiến trình bị PID tái sử dụng) -> coi tốc độ là 0 thay vì âm.
                    readSpeed = deltaRead > 0 ? deltaRead / elapsedSeconds : 0;
                    writeSpeed = deltaWrite > 0 ? deltaWrite / elapsedSeconds : 0;
                }
            }

            _previousSamples[pid] = new Sample
            {
                ReadBytesTotal = readBytes,
                WriteBytesTotal = writeBytes,
                Timestamp = now
            };

            return new IoStats
            {
                Pid = pid,
                ReadBytesTotal = readBytes,
                WriteBytesTotal = writeBytes,
                ReadSpeedBps = Math.Round(readSpeed, 0),
                WriteSpeedBps = Math.Round(writeSpeed, 0),
                SampledAt = now
            };
        }

        /// <summary>Dọn mẫu cũ của tiến trình đã kết thúc, gọi sau mỗi lần quét toàn bộ danh sách.</summary>
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

        private static long ParseValue(string line, string prefix)
        {
            var value = line.Substring(prefix.Length).Trim();
            return long.TryParse(value, out var result) ? result : 0;
        }
    }
}