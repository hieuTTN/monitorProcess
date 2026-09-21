using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using monitorProcess.Services;
using Avalonia.Input;
using Avalonia;
using Avalonia.Media;

namespace monitorProcess.controllers;

public partial class ProcessListView : UserControl
{
    private readonly UserResolverService _userResolver = new();
    private readonly ProcessEnumeratorService _enumerator;
    private readonly PathAnomalyDetectionService _pathChecker = new();
    private readonly HashService _hashService = new();
    private readonly DangerousProcessDbService _dangerousDb;
    private readonly NetworkMonitorService _networkMonitor = new();
    private readonly ProcessControlService _controlService = new();
    private readonly ResourceMonitorService _resourceMonitor = new();
    private readonly IoMonitorService _ioMonitor = new();
    private readonly ReportExportService _reportExport = new();

    private readonly WhitelistService _whitelist = AppServices.Whitelist;

    // Tập PID đã ghi nhận ở lần quét TRƯỚC - dùng để phân biệt tiến trình "đã
    // chạy từ trước" (baseline, không đụng tới) với tiến trình "mới sinh ra sau
    // khi bật giám sát" (đối tượng duy nhất bị tự động chặn nếu không có whitelist).
    private HashSet<int>? _knownPidsFromPreviousScan;

    private bool _isViewReady;

    private readonly ResourceThresholdDetectionService _thresholdChecker = AppServices.Thresholds;

    private readonly ObservableCollection<ProcessRowViewModel> _displayedRows = new();
    private List<ProcessRowViewModel> _allRowsCache = new();
    private DispatcherTimer? _autoRefreshTimer;

    public ProcessListView()
    {
        InitializeComponent();

        _enumerator = new ProcessEnumeratorService(_userResolver);
        // Không dùng whitelist nữa - chỉ so khớp với CSDL tiến trình nguy hiểm.
        _dangerousDb = new DangerousProcessDbService("Data/dangerous_processes.json", _hashService);

        var lst = this.FindControl<ListBox>("LstProcesses");
        if (lst != null) lst.ItemsSource = _displayedRows;

        LoadProcesses();
        SetupAutoRefresh();
        _isViewReady = true;
    }

    public void LoadProcesses()
    {
        var processes = _enumerator.GetAllProcesses();

        // Xác định tiến trình nào là MỚI SINH RA kể từ lần quét trước - đây là
        // điều kiện tiên quyết để quyết định có được phép tự động kill (whitelist)
        // hay không. Lần quét ĐẦU TIÊN khi app vừa mở: coi TOÀN BỘ tiến trình đang
        // chạy là "đã có từ trước" (baseline) - không kill ai ở lần này.
        var currentPids = processes.Select(x => x.Pid).ToHashSet();
        var isFirstScan = _knownPidsFromPreviousScan == null;
        var newlyAppearedPids = isFirstScan
            ? new HashSet<int>()
            : currentPids.Except(_knownPidsFromPreviousScan!).ToHashSet();
        _knownPidsFromPreviousScan = currentPids;

        // Quét toàn bộ kết nối mạng 1 lần duy nhất, sau đó nhóm theo PID -
        // tránh phải quét /proc/net + /proc/[pid]/fd lặp lại cho từng tiến trình.
        var allConnections = _networkMonitor.GetAllConnections();
        var connectionsByPid = allConnections
            .Where(c => c.Pid != 0)
            .GroupBy(c => c.Pid)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ProcessRowViewModel>();

        foreach (var p in processes.OrderBy(x => x.Pid))
        {
            var alerts = new List<string>();

            var pathAlert = _pathChecker.Check(p);
            if (pathAlert != null) alerts.Add(pathAlert.Message);

            var dbAlert = _dangerousDb.Check(p);
            if (dbAlert != null) alerts.Add(dbAlert.Message);

            // Kiểm tra Whitelist - CHỈ tự động kill nếu đây là tiến trình MỚI
            // sinh ra sau khi bắt đầu giám sát, KHÔNG đụng tới tiến trình đã
            // chạy ổn định từ trước (kể cả khi chúng không nằm trong whitelist).
            var wlAlert = _whitelist.Check(p);
            if (wlAlert != null)
            {
                var isNewProcess = newlyAppearedPids.Contains(p.Pid);

                alerts.Add(isNewProcess
                    ? wlAlert.Message + " [MỚI - đã tự động chặn]"
                    : wlAlert.Message + " [Đã chạy từ trước khi bật giám sát - không tự động xử lý]");

                if (isNewProcess)
                {
                    var killResult = _controlService.Kill(p.Pid);
                    alerts.Add(killResult.Success
                        ? "→ Đã TỰ ĐỘNG KILL (tiến trình mới, không nằm trong whitelist)."
                        : $"→ Tự động kill thất bại: {killResult.Message}");
                }
            }

            var portsText = string.Empty;
            var listenPorts = new List<int>();
            if (connectionsByPid.TryGetValue(p.Pid, out var conns))
            {
                // Bỏ trùng (nhiều fd có thể trỏ tới cùng 1 cổng LISTEN).
                portsText = string.Join(", ", conns
                    .Select(c => string.IsNullOrEmpty(c.State)
                        ? $"{c.Protocol}:{c.LocalPort}"
                        : $"{c.Protocol}:{c.LocalPort} ({c.State})")
                    .Distinct());

                listenPorts = conns.Where(c => c.State == "LISTEN")
                    .Select(c => c.LocalPort).Distinct().ToList();
            }

            // Đo CPU%/RAM - lần đầu tiên gặp 1 PID sẽ luôn ra CPU% = 0 (chưa có mẫu để so sánh).
            var resource = _resourceMonitor.Measure(p.Pid);
            // Đo tốc độ I/O - lần đầu tiên gặp 1 PID sẽ luôn ra tốc độ = 0 (chưa có mẫu để so sánh).
            var io = _ioMonitor.Measure(p.Pid);

            // Kiểm tra ngưỡng CPU%/RAM% - cần dữ liệu resource nên đặt sau khi đo.
            var thresholdAlerts = _thresholdChecker.Check(
                p, resource?.CpuPercent ?? 0, resource?.MemoryPercent ?? 0);
            alerts.AddRange(thresholdAlerts.Select(a => a.Message));

            // Chấm điểm rủi ro theo 5 tiêu chí trọng số.
            var riskResult = AppServices.RiskScoring.Check(p, resource?.CpuPercent ?? 0, listenPorts);
            if (riskResult.IsDangerous)
            {
                alerts.Add($"[RỦI RO {riskResult.Score}/100] " + string.Join("; ", riskResult.Reasons));
            }

            rows.Add(new ProcessRowViewModel
            {
                Pid = p.Pid,
                Name = p.Name,
                UserName = p.UserName,
                State = p.State.ToString(),
                ExePath = string.IsNullOrEmpty(p.ExePath) ? "(không xác định)" : p.ExePath,
                CmdLine = p.CmdLine,
                Ports = portsText,
                CpuPercent = resource?.CpuPercent ?? 0,
                MemoryMb = resource != null ? resource.MemoryKb / 1024.0 : 0,
                MemoryPercent = resource?.MemoryPercent ?? 0,
                ReadSpeedBps = io?.ReadSpeedBps ?? 0,
                WriteSpeedBps = io?.WriteSpeedBps ?? 0,
                AlertText = alerts.Count > 0 ? string.Join(" | ", alerts) : "",
                IsSuspicious = alerts.Count > 0,
                RiskScore = riskResult.Score,
                CategoryText = (p.Uid == 0 || p.Uid < 1000) ? "Hệ thống" : "Người dùng",
            });
        }

        // Dọn mẫu CPU cũ của các tiến trình đã kết thúc, tránh rò rỉ bộ nhớ theo thời gian.
        _resourceMonitor.ForgetDeadProcesses(rows.Select(r => r.Pid));
        _ioMonitor.ForgetDeadProcesses(rows.Select(r => r.Pid));
        _allRowsCache = rows;
        ApplyFilter();

        // Gửi cảnh báo cho các vi phạm MỚI qua Telegram/Email (fire-and-forget:
        // không "await" nên không cần LoadProcesses() phải là async, task tự
        // chạy nền và không làm chậm/giật giao diện).
        _ = AppServices.Notifications.NotifyNewAlertsAsync(rows);
        AppServices.Notifications.ForgetDeadProcesses(rows.Select(r => r.Pid));
    }

    private void ApplyFilter()
    {
        var txtSearch = this.FindControl<TextBox>("TxtSearch");
        var keyword = txtSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(keyword)
            ? _allRowsCache
            : _allRowsCache.Where(r =>
                r.Name.ToLowerInvariant().Contains(keyword) ||
                r.Pid.ToString().Contains(keyword) ||
                r.Ports.ToLowerInvariant().Contains(keyword)).ToList();
        filtered = ApplySort(filtered);

        _displayedRows.Clear();
        foreach (var row in filtered) _displayedRows.Add(row);
    }

    /// <summary>Sắp xếp danh sách theo tiêu chí đang chọn ở ComboBox "CboSortBy".</summary>
    private List<ProcessRowViewModel> ApplySort(IEnumerable<ProcessRowViewModel> rows)
    {
        var combo = this.FindControl<ComboBox>("CboSortBy");
        var index = combo?.SelectedIndex ?? 0;

        return index switch
        {
            1 => rows.OrderByDescending(r => r.IsSuspicious)
                    .ThenByDescending(r => r.CpuPercent)
                    .ToList(),

            2 => rows.OrderByDescending(r => r.CpuPercent).ToList(),

            3 => rows.OrderByDescending(r => r.MemoryMb).ToList(),

            4 => rows.OrderByDescending(r => r.ReadSpeedBps).ToList(),

            5 => rows.OrderByDescending(r => r.WriteSpeedBps).ToList(),

            6 => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),

            _ => rows.OrderBy(r => r.Pid).ToList()
        };
    }

    private void SetupAutoRefresh()
    {
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoRefreshTimer.Tick += (_, _) =>
        {
            var chk = this.FindControl<CheckBox>("ChkAutoRefresh");
            if (chk?.IsChecked == true) LoadProcesses();
        };
        _autoRefreshTimer.Start();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => LoadProcesses();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isViewReady) return;
        ApplyFilter();
    }

    private void OnKillClick(object? sender, RoutedEventArgs e) =>
        ExecuteAction(sender, pid => _controlService.Kill(pid));

    private void OnStopClick(object? sender, RoutedEventArgs e) =>
        ExecuteAction(sender, pid => _controlService.Stop(pid));

    private void OnContinueClick(object? sender, RoutedEventArgs e) =>
        ExecuteAction(sender, pid => _controlService.Continue(pid));

    private void OnRestartClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control ctrl || ctrl.Tag is not int pid)
            return;

        var row = _allRowsCache.FirstOrDefault(r => r.Pid == pid);
        if (row == null)
        {
            ShowStatus(false, $"Không tìm thấy thông tin tiến trình PID {pid} để khởi động lại.");
            return;
        }

        var result = _controlService.Restart(pid, row.ExePath, row.CmdLine);
        ShowStatus(result.Success, result.Message);
        LoadProcesses();
    }

    private void ExecuteAction(object? sender, Func<int, ProcessActionResult> action)
    {
        if (sender is not Control ctrl || ctrl.Tag is not int pid)
            return;

        var result = action(pid);
        ShowStatus(result.Success, result.Message);
        LoadProcesses();
    }

    private void ShowStatus(bool success, string message)
    {
        var txtStatus = this.FindControl<TextBlock>("TxtStatus");
        if (txtStatus == null) return;

        txtStatus.Text = message;
        txtStatus.Foreground = success ? Brushes.LightGreen : Brushes.OrangeRed;
    }


     // ----- Xuất báo cáo -----

    private void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var outputDir = GetReportDirectory();
            var (validPath, violationPath) = _reportExport.ExportCsv(_allRowsCache, outputDir);

            ShowStatus(true,
                $"Đã xuất CSV thành công:\n- Hợp lệ: {validPath}\n- Vi phạm: {violationPath}");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Lỗi khi xuất CSV: {ex.Message}");
        }
    }

    private void OnExportPdfClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var outputDir = GetReportDirectory();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var pdfPath = Path.Combine(outputDir, $"bao_cao_tien_trinh_{timestamp}.pdf");

            _reportExport.ExportPdf(_allRowsCache, pdfPath);

            ShowStatus(true, $"Đã xuất PDF thành công: {pdfPath}");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Lỗi khi xuất PDF: {ex.Message}");
        }
    }

    /// <summary>Thư mục lưu báo cáo, đặt trong thư mục home của user hiện tại để không cần quyền đặc biệt.</summary>
    // private static string GetReportDirectory()
    // {
    //     var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    //     return Path.Combine(home, "ProcessMonitorReports");
    // }


    /// <summary>
    /// Thư mục lưu báo cáo - đặt tại Desktop của user THẬT (không phải root),
    /// kể cả khi ứng dụng đang chạy bằng sudo.
    ///
    /// Khi chạy "sudo dotnet run", Environment.SpecialFolder.UserProfile trả về
    /// "/root" (home của root) chứ không phải home thật của user - vì lúc đó
    /// tiến trình đang thực thi dưới danh nghĩa UID 0 (root). Để lấy lại đúng
    /// user gốc, đọc biến môi trường SUDO_USER mà lệnh sudo tự động gán.
    /// </summary>
    private static string GetReportDirectory()
    {
        var home = GetRealUserHomeDirectory();
        var desktop = Path.Combine(home, "Desktop");

        Directory.CreateDirectory(desktop);

        return Path.Combine(desktop, "ProcessMonitorReports");
    }

    private static string GetRealUserHomeDirectory()
    {
        // Nếu đang chạy qua sudo, SUDO_USER chứa tên user gốc (vd "hieutv").
        var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");

        if (!string.IsNullOrEmpty(sudoUser))
        {
            var sudoUserHome = $"/home/{sudoUser}";
            if (Directory.Exists(sudoUserHome))
                return sudoUserHome;
        }

        // Không chạy bằng sudo -> home hiện tại đã đúng là của user thật.
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
    

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Chỉ mở chi tiết khi bấm CHUỘT TRÁI - chuột phải dành riêng cho ContextMenu.
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed)
            return;

        if (sender is not Border border || border.DataContext is not ProcessRowViewModel row)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var detailWindow = new ProcessDetailWindow(row);

        if (owner != null)
            detailWindow.ShowDialog(owner);
        else
            detailWindow.Show();
    }

}