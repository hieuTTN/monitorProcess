using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Liệt kê các ứng dụng/dịch vụ được cấu hình để TỰ CHẠY khi khởi động máy
    /// hoặc khi đăng nhập - tương đương tab "Startup Apps" của Task Manager
    /// trên Windows. Trên Linux đây là 2 cơ chế tách biệt, xem thêm giải thích
    /// trong enum StartupSource ở models/StartupItem.cs.
    /// </summary>
    public class StartupAppsService
    {
        /// <summary>Lấy toàn bộ danh sách khởi động cùng máy/phiên đăng nhập.</summary>
        public List<StartupItem> GetStartupItems()
        {
            var items = new List<StartupItem>();

            items.AddRange(ScanDesktopAutostartDir("/etc/xdg/autostart", StartupSource.DesktopAutostartSystem));

            var userAutostartDir = GetUserAutostartDir();
            if (!string.IsNullOrEmpty(userAutostartDir))
                items.AddRange(ScanDesktopAutostartDir(userAutostartDir, StartupSource.DesktopAutostartUser));

            items.AddRange(GetEnabledSystemdServices());

            return items.OrderBy(i => i.Source).ThenBy(i => i.Name).ToList();
        }

        /// <summary>
        /// Đổi trạng thái bật/tắt của 1 mục khởi động. Cách làm khác nhau tuỳ
        /// nguồn gốc - xem giải thích chi tiết trong từng hàm con bên dưới.
        /// </summary>
        public (bool success, string message) ToggleEnabled(StartupItem item, bool enable)
        {
            return item.Source switch
            {
                StartupSource.DesktopAutostartUser => SetUserDesktopEntryEnabled(item.FilePath, enable),
                StartupSource.DesktopAutostartSystem => SetSystemDesktopEntryOverride(item, enable),
                StartupSource.SystemdService => SetSystemdServiceEnabled(item.Name, enable),
                _ => (false, "Không hỗ trợ bật/tắt loại mục này.")
            };
        }

        /// <summary>
        /// Sửa TRỰC TIẾP file .desktop trong ~/.config/autostart - vì đây là file
        /// CỦA RIÊNG người dùng hiện tại, sửa không ảnh hưởng ai khác.
        /// Ghi/đổi dòng "X-GNOME-Autostart-enabled=true|false" trong section
        /// [Desktop Entry] (thêm dòng này nếu file chưa có).
        /// </summary>
        private (bool success, string message) SetUserDesktopEntryEnabled(string filePath, bool enable)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, $"Không tìm thấy file '{filePath}'.");

                var lines = File.ReadAllLines(filePath).ToList();
                bool found = false;
                bool inDesktopEntrySection = false;
                int desktopEntryHeaderIndex = -1;

                for (int i = 0; i < lines.Count; i++)
                {
                    var trimmed = lines[i].Trim();

                    if (trimmed.StartsWith("["))
                    {
                        inDesktopEntrySection = trimmed.Equals("[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                        if (inDesktopEntrySection) desktopEntryHeaderIndex = i;
                        continue;
                    }

                    if (inDesktopEntrySection && trimmed.StartsWith("X-GNOME-Autostart-enabled=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"X-GNOME-Autostart-enabled={(enable ? "true" : "false")}";
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (desktopEntryHeaderIndex < 0)
                        return (false, "File .desktop không hợp lệ - thiếu section [Desktop Entry].");

                    lines.Insert(desktopEntryHeaderIndex + 1,
                        $"X-GNOME-Autostart-enabled={(enable ? "true" : "false")}");
                }

                File.WriteAllLines(filePath, lines);
                return (true, $"Đã {(enable ? "BẬT" : "TẮT")} '{Path.GetFileName(filePath)}'.");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi khi sửa file: {ex.Message}");
            }
        }

        /// <summary>
        /// KHÔNG sửa file gốc trong /etc/xdg/autostart vì đó là cấu hình DÙNG
        /// CHUNG cho mọi người dùng trên máy - sửa thẳng vào đó sẽ ảnh hưởng
        /// tới cả những người dùng khác. Thay vào đó dùng đúng cơ chế chuẩn của
        /// XDG: tạo 1 file "override" CÙNG TÊN trong thư mục autostart riêng
        /// của người dùng hiện tại, chỉ chứa "Hidden=true" - file này sẽ được
        /// desktop ưu tiên đọc trước, ẩn hẳn mục hệ thống chỉ với riêng mình,
        /// người dùng khác trên máy không bị ảnh hưởng.
        /// </summary>
        private (bool success, string message) SetSystemDesktopEntryOverride(StartupItem item, bool enable)
        {
            try
            {
                var userDir = GetUserAutostartDir();
                if (string.IsNullOrEmpty(userDir))
                    return (false, "Không xác định được thư mục autostart của người dùng hiện tại.");

                var overridePath = Path.Combine(userDir, Path.GetFileName(item.FilePath));

                if (enable)
                {
                    // Muốn BẬT lại mục hệ thống -> chỉ cần XOÁ file override (nếu có),
                    // để desktop quay về đọc bản gốc trong /etc/xdg/autostart.
                    if (File.Exists(overridePath))
                        File.Delete(overridePath);

                    return (true, $"Đã BẬT lại '{item.Name}' (xoá file ghi đè riêng, dùng lại cấu hình hệ thống).");
                }
                else
                {
                    Directory.CreateDirectory(userDir);
                    File.WriteAllText(overridePath, "[Desktop Entry]\nHidden=true\n");
                    return (true, $"Đã TẮT '{item.Name}' CHỈ CHO người dùng hiện tại (không ảnh hưởng người dùng khác trên máy).");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi khi ghi file override: {ex.Message}");
            }
        }

        /// <summary>
        /// Bật/tắt 1 dịch vụ systemd bằng "systemctl enable/disable". CHỈ ngăn
        /// dịch vụ tự chạy ở LẦN KHỞI ĐỘNG KẾ TIẾP - KHÔNG dừng dịch vụ đang
        /// chạy ngay bây giờ. Đây là lựa chọn CÓ CHỦ ĐÍCH: nếu tự động dừng
        /// luôn dịch vụ đang chạy, rất dễ tự cắt đứt phiên làm việc của chính
        /// bạn (ví dụ lỡ tắt "sshd" trong khi đang điều khiển máy qua SSH).
        /// Muốn dừng ngay, dùng chức năng "Kill tiến trình" đã có sẵn trong
        /// màn hình Tiến trình.
        /// </summary>
        private (bool success, string message) SetSystemdServiceEnabled(string serviceName, bool enable)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = $"{(enable ? "enable" : "disable")} {serviceName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return (false, "Không khởi chạy được lệnh systemctl.");

                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);

                if (proc.ExitCode == 0)
                {
                    return (true, $"Đã {(enable ? "BẬT" : "TẮT")} tự khởi động cho '{serviceName}' " +
                                  "(có hiệu lực từ LẦN KHỞI ĐỘNG KẾ TIẾP, dịch vụ đang chạy hiện tại không bị dừng).");
                }

                return (false, $"systemctl báo lỗi (mã {proc.ExitCode}): {stderr.Trim()}. " +
                                "Có thể cần chạy app bằng quyền root (sudo).");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi khi chạy systemctl: {ex.Message}");
            }
        }

        /// <summary>
        /// Xác định thư mục autostart CỦA NGƯỜI DÙNG THẬT, không phải của root.
        /// App này thường chạy bằng "sudo" (để ExecGuardService hoạt động được),
        /// nên $HOME lúc đó sẽ là "/root" chứ không phải thư mục người dùng
        /// thật - phải lấy qua biến môi trường SUDO_USER mà lệnh sudo tự đặt.
        /// </summary>
        public string GetUserAutostartDir()
        {
            var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
            if (!string.IsNullOrEmpty(sudoUser) && sudoUser != "root")
                return $"/home/{sudoUser}/.config/autostart";

            var home = Environment.GetEnvironmentVariable("HOME");
            return string.IsNullOrEmpty(home) ? "" : Path.Combine(home, ".config/autostart");
        }

        private List<StartupItem> ScanDesktopAutostartDir(string dir, StartupSource source)
        {
            var result = new List<StartupItem>();
            if (!Directory.Exists(dir))
                return result;

            foreach (var file in Directory.GetFiles(dir, "*.desktop"))
            {
                try
                {
                    var entry = ParseDesktopFile(file, source);
                    if (entry != null)
                        result.Add(entry);
                }
                catch
                {
                    // Bỏ qua file lỗi định dạng, không làm hỏng cả danh sách.
                }
            }

            return result;
        }

        /// <summary>
        /// Đọc file .desktop (định dạng INI đơn giản) - chỉ lấy các key cần
        /// thiết trong section [Desktop Entry].
        /// </summary>
        private StartupItem? ParseDesktopFile(string filePath, StartupSource source)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            string exec = "";
            string comment = "";
            bool hidden = false;
            bool gnomeAutostartEnabled = true;
            bool inDesktopEntrySection = false;

            foreach (var rawLine in File.ReadAllLines(filePath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("["))
                {
                    inDesktopEntrySection = line.Equals("[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inDesktopEntrySection)
                    continue;

                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line.Substring(0, idx).Trim();
                var value = line.Substring(idx + 1).Trim();

                switch (key)
                {
                    case "Name": name = value; break;
                    case "Exec": exec = value; break;
                    case "Comment": comment = value; break;
                    case "Hidden": hidden = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "X-GNOME-Autostart-enabled":
                        gnomeAutostartEnabled = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            return new StartupItem
            {
                Name = name,
                Command = exec,
                Description = comment,
                Source = source,
                Enabled = !hidden && gnomeAutostartEnabled,
                FilePath = filePath
            };
        }

        /// <summary>
        /// Lấy danh sách dịch vụ systemd đang được "enable" (tự chạy khi boot),
        /// bằng lệnh "systemctl list-unit-files" - lệnh này CHỈ ĐỌC, không cần
        /// quyền root (root chỉ cần khi BẬT/TẮT dịch vụ, không cần khi XEM).
        /// </summary>
        private List<StartupItem> GetEnabledSystemdServices()
        {
            var result = new List<StartupItem>();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = "list-unit-files --type=service --state=enabled --no-legend --no-pager",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                if (proc == null) return result;

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                foreach (var rawLine in output.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0) continue;

                    // Định dạng mỗi dòng: "ten-dich-vu.service    enabled"
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;

                    result.Add(new StartupItem
                    {
                        Name = parts[0],
                        Command = "",
                        Description = "Dịch vụ hệ thống - tự chạy khi máy khởi động, không cần đăng nhập",
                        Source = StartupSource.SystemdService,
                        Enabled = true,
                        FilePath = ""
                    });
                }
            }
            catch
            {
                // Không có lệnh "systemctl" (distro không dùng systemd) hoặc lỗi khác -> bỏ qua phần này.
            }

            return result;
        }
    }
}