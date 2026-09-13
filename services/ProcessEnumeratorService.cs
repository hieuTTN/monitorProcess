using System;
using System.Collections.Generic;
using System.IO;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Liệt kê toàn bộ tiến trình đang chạy bằng cách quét thư mục /proc.
    /// Tương ứng chức năng 1: Liệt kê danh sách tiến trình.
    ///
    /// Ghi chú: mỗi thư mục con dạng số trong /proc (ví dụ /proc/1234) tương ứng
    /// với 1 tiến trình có PID = 1234. Bên trong có các file ảo (không phải file
    /// thật trên đĩa) do kernel sinh ra để mô tả tiến trình đó.
    /// </summary>
    public class ProcessEnumeratorService
    {
        private const string ProcRoot = "/proc";
        private readonly UserResolverService _userResolver;

        public ProcessEnumeratorService(UserResolverService userResolver)
        {
            _userResolver = userResolver;
        }

        /// <summary>Lấy danh sách toàn bộ tiến trình hiện có trên hệ thống.</summary>
        public List<ProcessInfo> GetAllProcesses()
        {
            var result = new List<ProcessInfo>();

            foreach (var dir in Directory.EnumerateDirectories(ProcRoot))
            {
                var folderName = Path.GetFileName(dir);

                // Chỉ lấy các thư mục có tên là số nguyên (PID), bỏ qua
                // /proc/self, /proc/cpuinfo, /proc/net... vì đó không phải tiến trình.
                if (!int.TryParse(folderName, out var pid))
                    continue;

                var info = TryReadProcess(pid);
                if (info != null)
                    result.Add(info);
            }

            return result;
        }

        /// <summary>Đọc thông tin 1 tiến trình theo PID. Trả về null nếu tiến trình đã kết thúc hoặc không đọc được.</summary>
        public ProcessInfo? TryReadProcess(int pid)
        {
            var procDir = Path.Combine(ProcRoot, pid.ToString());

            try
            {
                var info = new ProcessInfo { Pid = pid };

                ReadStatusFile(procDir, info);
                info.CmdLine = ReadCmdLine(procDir);
                info.ExePath = ReadExePath(procDir);
                info.UserName = _userResolver.Resolve(info.Uid);

                return info;
            }
            catch (IOException)
            {
                // Tiến trình có thể đã kết thúc ngay giữa lúc đang đọc (race condition
                // rất bình thường khi quét /proc) -> bỏ qua, không coi là lỗi.
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                // Không đủ quyền đọc thông tin tiến trình của user khác -> bỏ qua.
                return null;
            }
        }

        /// <summary>
        /// Đọc /proc/[pid]/status - file text dạng "Key:\tValue" mỗi dòng,
        /// chứa Name, State, PPid, Uid...
        /// </summary>
        private void ReadStatusFile(string procDir, ProcessInfo info)
        {
            var statusPath = Path.Combine(procDir, "status");
            if (!File.Exists(statusPath))
                return;

            foreach (var line in File.ReadLines(statusPath))
            {
                if (line.StartsWith("Name:"))
                {
                    info.Name = line.Substring("Name:".Length).Trim();
                }
                else if (line.StartsWith("State:"))
                {
                    // Dạng: "State:\tS (sleeping)" -> ký tự đầu tiên sau khoảng trắng là mã trạng thái.
                    var value = line.Substring("State:".Length).Trim();
                    var stateChar = value.Length > 0 ? value[0] : ' ';
                    info.State = MapState(stateChar);
                }
                else if (line.StartsWith("PPid:"))
                {
                    var value = line.Substring("PPid:".Length).Trim();
                    int.TryParse(value, out var ppid);
                    info.Ppid = ppid;
                }
                else if (line.StartsWith("Uid:"))
                {
                    // Dạng: "Uid:\t1000\t1000\t1000\t1000" (Real, Effective, Saved, FS)
                    // -> lấy giá trị đầu tiên (Real UID).
                    var value = line.Substring("Uid:".Length).Trim();
                    var parts = value.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[0], out var uid))
                    {
                        info.Uid = uid;
                    }
                }
            }
        }

        /// <summary>
        /// Đọc /proc/[pid]/cmdline - các tham số được phân cách bằng ký tự NUL (\0)
        /// thay vì khoảng trắng, nên cần thay thế lại cho dễ đọc.
        /// </summary>
        private string ReadCmdLine(string procDir)
        {
            var cmdlinePath = Path.Combine(procDir, "cmdline");
            if (!File.Exists(cmdlinePath))
                return string.Empty;

            var bytes = File.ReadAllBytes(cmdlinePath);
            if (bytes.Length == 0)
                return string.Empty;

            var raw = System.Text.Encoding.UTF8.GetString(bytes);
            return raw.Replace('\0', ' ').Trim();
        }

        /// <summary>
        /// /proc/[pid]/exe là 1 symlink trỏ tới file thực thi thật trên đĩa.
        /// Với tiến trình kernel (kthread) hoặc tiến trình đã bị xoá file gốc,
        /// việc đọc symlink có thể thất bại - khi đó trả về rỗng.
        /// </summary>
        private string ReadExePath(string procDir)
        {
            var exePath = Path.Combine(procDir, "exe");
            try
            {
                var target = File.ResolveLinkTarget(exePath, returnFinalTarget: true);
                return target?.FullName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ProcessState MapState(char stateChar)
        {
            return stateChar switch
            {
                'R' => ProcessState.Running,
                'S' => ProcessState.Sleeping,
                'D' => ProcessState.UninterruptibleSleep,
                'T' => ProcessState.Stopped,
                't' => ProcessState.Stopped,
                'Z' => ProcessState.Zombie,
                'I' => ProcessState.Idle,
                _ => ProcessState.Unknown
            };
        }
    }
}