using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace monitorProcess.controllers;

public partial class ProcessDetailWindow : Window
{
    public ProcessDetailWindow()
    {
        InitializeComponent();
    }

    public ProcessDetailWindow(ProcessRowViewModel row) : this()
    {
        this.FindControl<TextBlock>("TxtTitle")!.Text = $"Chi tiết tiến trình - PID {row.Pid}";

        if (row.IsSuspicious)
        {
            this.FindControl<Border>("AlertBox")!.IsVisible = true;
            this.FindControl<TextBlock>("TxtAlertDetail")!.Text = row.AlertText;
        }

        var panel = this.FindControl<StackPanel>("PanelDetails")!;

        AddRow(panel, "PID", row.Pid.ToString());
        AddRow(panel, "Tên tiến trình", row.Name);
        AddRow(panel, "Người dùng sở hữu", row.UserName);
        AddRow(panel, "Loại tiến trình", row.CategoryText);
        AddRow(panel, "Trạng thái", row.State);
        AddRow(panel, "Đường dẫn thực thi", row.ExePath);
        AddRow(panel, "Câu lệnh khởi chạy", string.IsNullOrEmpty(row.CmdLine) ? "(không xác định)" : row.CmdLine);
        AddRow(panel, "CPU", $"{row.CpuPercent:0.0}%");
        AddRow(panel, "RAM", $"{row.MemoryMb:0.0} MB ({row.MemoryPercent:0.0}%)");
        AddRow(panel, "Tốc độ đọc đĩa", FormatSpeed(row.ReadSpeedBps));
        AddRow(panel, "Tốc độ ghi đĩa", FormatSpeed(row.WriteSpeedBps));
        AddRow(panel, "Cổng mạng", string.IsNullOrEmpty(row.Ports) ? "(không có)" : row.Ports);
        AddRow(panel, "Điểm rủi ro", $"{row.RiskScore}/100");
    }

    private void AddRow(StackPanel panel, string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*") };

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#6B6B76")),
            FontSize = 12
        };
        Grid.SetColumn(lbl, 0);

        var val = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.Parse("#1A1A1A")),
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        Grid.SetColumn(val, 1);

        grid.Children.Add(lbl);
        grid.Children.Add(val);
        panel.Children.Add(grid);
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0.0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}