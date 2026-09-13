using Avalonia;
using System;


namespace monitorProcess;
class Run
{
    // Cần có thuộc tính [STAThread] cho ứng dụng GUI
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Cấu hình Avalonia App
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}