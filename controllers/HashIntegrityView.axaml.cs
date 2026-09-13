using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using monitorProcess.Services;

namespace monitorProcess.controllers;

public partial class HashIntegrityView : UserControl
{

    private List<Models.KnownHashEntry> _allEntriesCache = new();

    public HashIntegrityView()
    {
        InitializeComponent();
        RefreshKnownHashesList();
    }

    private void RefreshKnownHashesList()
    {
        _allEntriesCache = AppServices.RiskScoring.GetAllKnownHashes().ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var txtSearch = this.FindControl<TextBox>("TxtSearch");
        var keyword = txtSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(keyword)
            ? _allEntriesCache
            : _allEntriesCache.Where(x =>
                x.ExePath.ToLowerInvariant().Contains(keyword) ||
                x.Description.ToLowerInvariant().Contains(keyword)).ToList();

        var lst = this.FindControl<ListBox>("LstKnownHashes");
        if (lst != null) lst.ItemsSource = filtered;

        var txtCount = this.FindControl<TextBlock>("TxtCount");
        if (txtCount != null)
            txtCount.Text = filtered.Count == _allEntriesCache.Count
                ? $"({_allEntriesCache.Count} file)"
                : $"({filtered.Count}/{_allEntriesCache.Count} file)";
    }

    private void OnAddKnownHashClick(object? sender, RoutedEventArgs e)
    {
        var path = this.FindControl<TextBox>("TxtNewKnownPath")!.Text?.Trim() ?? string.Empty;
        var desc = this.FindControl<TextBox>("TxtNewKnownDesc")!.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(path))
        {
            ShowStatus(false, "Vui lòng nhập đường dẫn file.");
            return;
        }

        var (success, message) = AppServices.RiskScoring.AddKnownFile(path, desc);
        ShowStatus(success, message);

        if (success)
        {
            ClearForm();
            RefreshKnownHashesList();
        }
    }

    private void OnSaveManualHashClick(object? sender, RoutedEventArgs e)
    {
        var path = this.FindControl<TextBox>("TxtNewKnownPath")!.Text?.Trim() ?? string.Empty;
        var hash = this.FindControl<TextBox>("TxtNewKnownHash")!.Text?.Trim() ?? string.Empty;
        var desc = this.FindControl<TextBox>("TxtNewKnownDesc")!.Text?.Trim() ?? string.Empty;

        var (success, message) = AppServices.RiskScoring.SetKnownHash(path, hash, desc);
        ShowStatus(success, message);

        if (success)
        {
            ClearForm();
            RefreshKnownHashesList();
        }
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnEditKnownHashClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string exePath)
            return;

        var entry = AppServices.RiskScoring.GetAllKnownHashes().FirstOrDefault(x => x.ExePath == exePath);
        if (entry == null) return;

        this.FindControl<TextBox>("TxtNewKnownPath")!.Text = entry.ExePath;
        this.FindControl<TextBox>("TxtNewKnownHash")!.Text = entry.Sha256;
        this.FindControl<TextBox>("TxtNewKnownDesc")!.Text = entry.Description;

        ShowStatus(true, $"Đang sửa {exePath} - chỉnh nội dung rồi bấm \"Lưu hash thủ công\" (giữ hash đã sửa tay) hoặc \"Tự tính hash từ file\" (tính lại từ file thật) để cập nhật.");
    }

    private void OnRemoveKnownHashClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string exePath)
            return;

        AppServices.RiskScoring.RemoveKnownFile(exePath);
        ShowStatus(true, $"Đã xóa {exePath} khỏi CSDL đáng tin cậy.");
        RefreshKnownHashesList();
    }

    private void OnClearKnownHashFormClick(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        this.FindControl<TextBox>("TxtNewKnownPath")!.Text = string.Empty;
        this.FindControl<TextBox>("TxtNewKnownHash")!.Text = string.Empty;
        this.FindControl<TextBox>("TxtNewKnownDesc")!.Text = string.Empty;
    }

    private void ShowStatus(bool success, string message)
    {
        var txtStatus = this.FindControl<TextBlock>("TxtStatus");
        if (txtStatus == null) return;

        txtStatus.Text = message;
        txtStatus.Foreground = success ? Brushes.LightGreen : Brushes.OrangeRed;
    }
}