using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace monitorProcess.controllers;

public partial class MainWindow : Window
{
    private readonly DashboardView _dashboardView = new();
    private readonly ProcessListView _processListView = new();
    private readonly SettingsView _settingsView = new();
    private readonly HashIntegrityView _hashIntegrityView = new();
    private readonly WhitelistView _whitelistView = new();
    private readonly StartupAppsView _startupAppsView = new();

    public MainWindow()
    {
        InitializeComponent();
        // Mặc định hiển thị Dashboard khi khởi chạy (khớp với nút đã đánh
        // dấu "active" sẵn trong XAML).
        MainContentArea.Content = _dashboardView;
    }

    private void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedBtn)
            return;

        // Gỡ class "active" khỏi TẤT CẢ nút điều hướng, sau đó chỉ gán lại
        // cho nút vừa được bấm - đảm bảo luôn chỉ có đúng 1 nút active.
        foreach (var btn in GetNavButtons())
        {
            btn.Classes.Remove("active");
        }
        clickedBtn.Classes.Add("active");

        switch (clickedBtn.Name)
        {
            case "BtnDashboard":
                MainContentArea.Content = _dashboardView;
                break;

            case "BtnProcess":
                MainContentArea.Content = _processListView;
                break;

            case "BtnSettings":
                MainContentArea.Content = _settingsView;
                break;

            case "BtnHashIntegrity":
                MainContentArea.Content = _hashIntegrityView;
                break;

            case "BtnWhitelist":
                MainContentArea.Content = _whitelistView;
                break;

            case "BtnStartupApps":
                MainContentArea.Content = _startupAppsView;
                break;
        }
    }

    /// <summary>
    /// Lấy toàn bộ nút điều hướng trong sidebar. Nếu sau này thêm nút mới,
    /// chỉ cần thêm Classes="navBtn" trong XAML và thêm tên nút vào đây.
    /// </summary>
    private IEnumerable<Button> GetNavButtons()
    {
        return new[] { BtnDashboard, BtnProcess,BtnWhitelist, BtnSettings, BtnHashIntegrity, BtnStartupApps };
    }
}