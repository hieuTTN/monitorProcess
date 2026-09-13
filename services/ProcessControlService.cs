using System;
using System.Diagnostics;
using System.Linq;

namespace monitorProcess.Services
{
    /// <summary>
    /// Can thiệp trực tiếp vào tiến trình: dừng (SIGSTOP), tiếp tục (SIGCONT),
    /// tiêu diệt (SIGKILL), và khởi động lại (kill + chạy lại từ cmdline cũ).
    /// Tương ứng chức năng 5: Can thiệp tiến trình (Process Control).
    ///
    /// Lưu ý: hầu hết thao tác này cần chạy ứng dụng với quyền đủ cao (root/sudo)
    /// để can thiệp vào tiến trình của user khác.
    /// </summary>
    public class ProcessControlService
    {
        /// <summary>Tiêu diệt tiến trình ngay lập tức (SIGKILL, không thể chặn/bỏ qua).</summary>
        public ProcessActionResult Kill(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                return ProcessActionResult.Ok($"Đã tiêu diệt (kill) tiến trình PID {pid}.");
            }
            catch (ArgumentException)
            {
                return ProcessActionResult.Fail($"Tiến trình PID {pid} không tồn tại (có thể đã kết thúc).");
            }
            catch (Exception ex)
            {
                return ProcessActionResult.Fail($"Không thể kill PID {pid}: {ex.Message}");
            }
        }

        /// <summary>Tạm dừng tiến trình (gửi tín hiệu SIGSTOP qua lệnh kill của hệ thống).</summary>
        public ProcessActionResult Stop(int pid) => SendSignal(pid, "-STOP", "tạm dừng (SIGSTOP)");

        /// <summary>Cho tiến trình đang bị tạm dừng chạy tiếp (gửi tín hiệu SIGCONT).</summary>
        public ProcessActionResult Continue(int pid) => SendSignal(pid, "-CONT", "tiếp tục (SIGCONT)");

        /// <summary>
        /// Khởi động lại tiến trình: kill tiến trình hiện tại rồi chạy lại từ
        /// đường dẫn thực thi + tham số dòng lệnh cũ.
        /// Lưu ý: đây là mô phỏng ở mức cơ bản, không đảm bảo khôi phục đúng
        /// trạng thái/kết nối mà tiến trình gốc đang có trước khi bị kill.
        /// </summary>
        public ProcessActionResult Restart(int pid, string exePath, string cmdLine)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                return ProcessActionResult.Fail(
                    $"Không thể khởi động lại PID {pid}: không xác định được đường dẫn file thực thi.");
            }

            var killResult = Kill(pid);
            if (!killResult.Success)
                return killResult;

            try
            {
                // cmdLine đọc từ /proc/[pid]/cmdline đã gộp thành 1 chuỗi phân
                // cách bởi khoảng trắng; bỏ phần tử đầu tiên (chính là exePath).
                var args = cmdLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1);
                var argString = string.Join(' ', args);

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = argString,
                    UseShellExecute = false
                };

                Process.Start(startInfo);

                return ProcessActionResult.Ok(
                    $"Đã kill PID {pid} cũ và khởi động lại tiến trình từ: {exePath}");
            }
            catch (Exception ex)
            {
                return ProcessActionResult.Fail(
                    $"Đã kill PID {pid} nhưng không khởi động lại được: {ex.Message}");
            }
        }

        /// <summary>Gửi tín hiệu bất kỳ tới tiến trình thông qua lệnh "kill" của hệ thống.</summary>
        private ProcessActionResult SendSignal(int pid, string signalArg, string actionDescription)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "kill",
                    Arguments = $"{signalArg} {pid}",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit(3000);

                if (process != null && process.ExitCode == 0)
                {
                    return ProcessActionResult.Ok($"Đã gửi tín hiệu {actionDescription} tới PID {pid}.");
                }

                var error = process?.StandardError.ReadToEnd();
                return ProcessActionResult.Fail(
                    $"Không thể gửi tín hiệu {actionDescription} tới PID {pid}. {error}".Trim());
            }
            catch (Exception ex)
            {
                return ProcessActionResult.Fail($"Lỗi khi gửi tín hiệu tới PID {pid}: {ex.Message}");
            }
        }
    }

    /// <summary>Kết quả trả về sau khi thực hiện 1 thao tác can thiệp tiến trình.</summary>
    public class ProcessActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public static ProcessActionResult Ok(string message) => new() { Success = true, Message = message };
        public static ProcessActionResult Fail(string message) => new() { Success = false, Message = message };
    }
}