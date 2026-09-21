namespace monitorProcess.Models
{
    /// <summary>Nguồn gốc của 1 mục khởi động cùng máy/phiên đăng nhập.</summary>
    public enum StartupSource
    {
        /// <summary>File .desktop trong /etc/xdg/autostart - áp dụng cho MỌI người dùng.</summary>
        DesktopAutostartSystem,

        /// <summary>File .desktop trong ~/.config/autostart - CHỈ riêng người dùng hiện tại.</summary>
        DesktopAutostartUser,

        /// <summary>Dịch vụ systemd đã "enable" - tự chạy khi máy khởi động, không cần đăng nhập.</summary>
        SystemdService
    }

    /// <summary>Một mục trong danh sách "chạy cùng khi khởi động" - tương đương 1 dòng trong tab Startup Apps của Windows.</summary>
    public class StartupItem
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public StartupSource Source { get; set; }
        public bool Enabled { get; set; } = true;
        public string FilePath { get; set; } = string.Empty;

        public string SourceLabel => Source switch
        {
            StartupSource.DesktopAutostartSystem => "Autostart (toàn hệ thống)",
            StartupSource.DesktopAutostartUser => "Autostart (người dùng)",
            StartupSource.SystemdService => "Dịch vụ systemd",
            _ => Source.ToString()
        };

        public string EnabledLabel => Enabled ? "Đang bật" : "Đã tắt";

        /// <summary>Màu hiển thị trạng thái - dùng trực tiếp cho binding Foreground trong XAML.</summary>
        public string EnabledColor => Enabled ? "LightGreen" : "OrangeRed";

        /// <summary>Nhãn nút bấm - luôn là hành động NGƯỢC LẠI với trạng thái hiện tại.</summary>
        public string ToggleLabel => Enabled ? "Tắt" : "Bật";
    }
}