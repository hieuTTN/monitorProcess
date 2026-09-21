using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia;
using monitorProcess.Services;

namespace monitorProcess.controllers;

public partial class DashboardView : UserControl
{
    private readonly SystemInfoService _systemInfo = new();
    private readonly List<double> _cpuHistory = new();
    private readonly List<double> _ramHistory = new();
    private const int MaxHistoryPoints = 30;
    private DispatcherTimer? _timer;

    public DashboardView()
    {
        InitializeComponent();

        LoadStaticInfo();
        RefreshMetrics();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => RefreshMetrics();
        _timer.Start();
    }

    private void LoadStaticInfo()
    {
        this.FindControl<TextBlock>("TxtHostname")!.Text = _systemInfo.GetHostname();
        this.FindControl<TextBlock>("TxtOsVersion")!.Text = _systemInfo.GetOsVersion();
        this.FindControl<TextBlock>("TxtKernel")!.Text = _systemInfo.GetKernelVersion();

        // Thông tin CPU - tĩnh, chỉ cần đọc 1 lần lúc khởi động.
        this.FindControl<TextBlock>("TxtCpuModel")!.Text = _systemInfo.GetCpuModelName();
        var (cores, logical) = _systemInfo.GetCpuCoreInfo();
        this.FindControl<TextBlock>("TxtCpuCores")!.Text = cores.ToString();
        this.FindControl<TextBlock>("TxtCpuLogical")!.Text = logical.ToString();
    }

    private void RefreshMetrics()
    {
        var snapshot = _systemInfo.Measure();

        this.FindControl<TextBlock>("TxtUptime")!.Text = _systemInfo.GetUptime();
        this.FindControl<TextBlock>("TxtUptime2")!.Text = _systemInfo.GetUptime();
        this.FindControl<TextBlock>("TxtProcessCount")!.Text = _systemInfo.GetProcessCount().ToString();

        this.FindControl<TextBlock>("TxtCpuPercent")!.Text = $"{snapshot.CpuPercent:0.0}%";
        this.FindControl<ProgressBar>("BarCpu")!.Value = Math.Min(snapshot.CpuPercent, 100);
        this.FindControl<TextBlock>("TxtCpuUtil")!.Text = $"{snapshot.CpuPercent:0.0}%";

        this.FindControl<TextBlock>("TxtRamPercent")!.Text = $"{snapshot.MemoryPercent:0.0}%";
        this.FindControl<ProgressBar>("BarRam")!.Value = Math.Min(snapshot.MemoryPercent, 100);
        this.FindControl<TextBlock>("TxtRamDetail")!.Text =
            $"{snapshot.MemoryUsedMb:0} / {snapshot.MemoryTotalMb:0} MB";

        this.FindControl<TextBlock>("TxtDiskRead")!.Text = $"Đọc: {FormatSpeed(snapshot.DiskReadBps)}";
        this.FindControl<TextBlock>("TxtDiskWrite")!.Text = $"Ghi: {FormatSpeed(snapshot.DiskWriteBps)}";

        this.FindControl<TextBlock>("TxtNetRx")!.Text = $"Nhận: {FormatSpeed(snapshot.NetRxBps)}";
        this.FindControl<TextBlock>("TxtNetTx")!.Text = $"Gửi: {FormatSpeed(snapshot.NetTxBps)}";

        UpdateCpuVisuals(snapshot.CpuPercent);


        this.FindControl<TextBlock>("TxtRamModel")!.Text =
            $"Tổng dung lượng: {snapshot.MemoryTotalMb:0} MB";
        this.FindControl<TextBlock>("TxtRamUtil")!.Text = $"{snapshot.MemoryPercent:0.0}%";
        this.FindControl<TextBlock>("TxtRamUsed")!.Text = $"{snapshot.MemoryUsedMb:0} MB";
        this.FindControl<TextBlock>("TxtRamTotal")!.Text = $"{snapshot.MemoryTotalMb:0} MB";
        this.FindControl<TextBlock>("TxtRamFree")!.Text =
            $"{(snapshot.MemoryTotalMb - snapshot.MemoryUsedMb):0} MB";

        UpdateRamVisuals(snapshot.MemoryPercent);
    }

    /// <summary>
    /// Cập nhật cả 2 hình thức trực quan hoá lịch sử CPU: bar-chart mini (card
    /// nhỏ ở hàng trên) và area chart lớn dạng Task Manager (khối chi tiết CPU).
    /// </summary>
    private void UpdateCpuVisuals(double cpuPercent)
    {
        _cpuHistory.Add(cpuPercent);
        if (_cpuHistory.Count > MaxHistoryPoints)
            _cpuHistory.RemoveAt(0);

        // --- Bar-chart mini (giữ nguyên logic cũ) ---
        var panel = this.FindControl<StackPanel>("SparkCpu");
        if (panel != null)
        {
            panel.Children.Clear();
            foreach (var value in _cpuHistory)
            {
                var barHeight = Math.Max(2, Math.Min(40, value / 100.0 * 40));
                panel.Children.Add(new Border
                {
                    Width = 6,
                    Height = barHeight,
                    Background = new SolidColorBrush(Color.Parse("#2ecc71")),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    CornerRadius = new Avalonia.CornerRadius(1)
                });
            }
        }

        // --- Area chart lớn (mới) ---
        DrawCpuAreaChart();
    }

    /// <summary>Vẽ biểu đồ vùng (area chart) CPU giống Task Manager, dùng Canvas + Polygon/Polyline vẽ thủ công.</summary>
    private void DrawCpuAreaChart()
    {
        var canvas = this.FindControl<Canvas>("CpuChartCanvas");
        if (canvas == null) return;

        var width = canvas.Bounds.Width;
        var height = canvas.Bounds.Height;

        // Canvas chưa layout xong (width/height = 0) -> bỏ qua, chu kỳ sau sẽ vẽ lại.
        if (width <= 0 || height <= 0 || _cpuHistory.Count < 2) return;

        canvas.Children.Clear();

        var stepX = width / (MaxHistoryPoints - 1);
        var startIndex = MaxHistoryPoints - _cpuHistory.Count;
        var points = new List<Point>();

        for (var i = 0; i < _cpuHistory.Count; i++)
        {
            var x = (startIndex + i) * stepX;
            var y = height - (_cpuHistory[i] / 100.0 * height);
            points.Add(new Point(x, y));
        }

        // Đóng đa giác xuống đáy canvas để tô được phần vùng dưới đường.
        var areaPoints = new List<Point>(points)
        {
            new Point(points[^1].X, height),
            new Point(points[0].X, height)
        };

        var area = new Polygon
        {
            Points = areaPoints,
            Fill = new SolidColorBrush(Color.Parse("#2ecc71"), 0.25)
        };

        var line = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.Parse("#2ecc71")),
            StrokeThickness = 2
        };

        canvas.Children.Add(area);
        canvas.Children.Add(line);
    }


        /// <summary>Cập nhật lịch sử % RAM và vẽ lại area chart RAM.</summary>
    private void UpdateRamVisuals(double ramPercent)
    {
        _ramHistory.Add(ramPercent);
        if (_ramHistory.Count > MaxHistoryPoints)
            _ramHistory.RemoveAt(0);

        DrawRamAreaChart();
    }

    /// <summary>Vẽ biểu đồ vùng (area chart) RAM, cùng cơ chế với DrawCpuAreaChart nhưng dùng màu xanh dương (#3498db) để đồng bộ với card RAM USAGE ở trên.</summary>
    private void DrawRamAreaChart()
    {
        var canvas = this.FindControl<Canvas>("RamChartCanvas");
        if (canvas == null) return;

        var width = canvas.Bounds.Width;
        var height = canvas.Bounds.Height;

        if (width <= 0 || height <= 0 || _ramHistory.Count < 2) return;

        canvas.Children.Clear();

        var stepX = width / (MaxHistoryPoints - 1);
        var startIndex = MaxHistoryPoints - _ramHistory.Count;
        var points = new List<Point>();

        for (var i = 0; i < _ramHistory.Count; i++)
        {
            var x = (startIndex + i) * stepX;
            var y = height - (_ramHistory[i] / 100.0 * height);
            points.Add(new Point(x, y));
        }

        var areaPoints = new List<Point>(points)
        {
            new Point(points[^1].X, height),
            new Point(points[0].X, height)
        };

        var area = new Polygon
        {
            Points = areaPoints,
            Fill = new SolidColorBrush(Color.Parse("#3498db"), 0.25)
        };

        var line = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.Parse("#3498db")),
            StrokeThickness = 2
        };

        canvas.Children.Add(area);
        canvas.Children.Add(line);
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0.0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }
}