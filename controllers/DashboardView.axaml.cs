using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using monitorProcess.Services;

namespace monitorProcess.controllers;

public partial class DashboardView : UserControl
{
    private readonly SystemInfoService _systemInfo = new();
    private readonly List<double> _cpuHistory = new();
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
    }

    private void RefreshMetrics()
    {
        var snapshot = _systemInfo.Measure();

        this.FindControl<TextBlock>("TxtUptime")!.Text = _systemInfo.GetUptime();

        this.FindControl<TextBlock>("TxtCpuPercent")!.Text = $"{snapshot.CpuPercent:0.0}%";
        this.FindControl<ProgressBar>("BarCpu")!.Value = Math.Min(snapshot.CpuPercent, 100);

        this.FindControl<TextBlock>("TxtRamPercent")!.Text = $"{snapshot.MemoryPercent:0.0}%";
        this.FindControl<ProgressBar>("BarRam")!.Value = Math.Min(snapshot.MemoryPercent, 100);
        this.FindControl<TextBlock>("TxtRamDetail")!.Text =
            $"{snapshot.MemoryUsedMb:0} / {snapshot.MemoryTotalMb:0} MB";

        this.FindControl<TextBlock>("TxtDiskRead")!.Text = $"Đọc: {FormatSpeed(snapshot.DiskReadBps)}";
        this.FindControl<TextBlock>("TxtDiskWrite")!.Text = $"Ghi: {FormatSpeed(snapshot.DiskWriteBps)}";

        this.FindControl<TextBlock>("TxtNetRx")!.Text = $"Nhận: {FormatSpeed(snapshot.NetRxBps)}";
        this.FindControl<TextBlock>("TxtNetTx")!.Text = $"Gửi: {FormatSpeed(snapshot.NetTxBps)}";

        UpdateCpuSparkline(snapshot.CpuPercent);
    }

    /// <summary>Vẽ sparkline CPU dạng bar-chart mini bằng các Border xếp ngang, chiều cao tỷ lệ với %CPU.</summary>
    private void UpdateCpuSparkline(double cpuPercent)
    {
        _cpuHistory.Add(cpuPercent);
        if (_cpuHistory.Count > MaxHistoryPoints)
            _cpuHistory.RemoveAt(0);

        var panel = this.FindControl<StackPanel>("SparkCpu");
        if (panel == null) return;

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

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024:0.0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }
}