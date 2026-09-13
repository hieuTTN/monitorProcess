using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using monitorProcess.controllers;
using monitorProcess.Services;

namespace monitorProcess; // Khai báo đúng namespace này

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Bật chặn thực thi TỪ ĐẦU (fanotify) - phải gọi trước khi mở MainWindow,
        // để không bỏ lỡ tiến trình nào cố chạy ngay từ lúc app vừa khởi động.
        // Cần chạy app với quyền root, nếu không sẽ tự tắt tính năng này và
        // in cảnh báo ra console (xem services/ExecGuardService.cs).
        AppServices.ExecGuard.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}