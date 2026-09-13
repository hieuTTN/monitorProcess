using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using monitorProcess.Models;
using monitorProcess.Services;
using System.Linq;

namespace monitorProcess.controllers;

public partial class WhitelistView : UserControl
{
    private readonly WhitelistService _whitelist = AppServices.Whitelist;
    private List<WhitelistEntry> _allEntriesCache = new();
    private List<ExecGuardSuggestion> _allSuggestionsCache = new();

    public WhitelistView()
    {
        InitializeComponent();

        this.FindControl<CheckBox>("ChkLogOnlyMode")!.IsChecked = AppServices.ExecGuard.LogOnlyMode;

        RefreshList();
        RefreshSuggestions();
    }

    // ---------- Danh sách whitelist hiện tại ----------

    private void RefreshList()
    {
        _whitelist.Load();
        _allEntriesCache = _whitelist.GetAll().ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var txtSearch = this.FindControl<TextBox>("TxtSearch");
        var keyword = txtSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(keyword)
            ? _allEntriesCache
            : _allEntriesCache.Where(x =>
                x.ProcessName.ToLowerInvariant().Contains(keyword)).ToList();

        var lst = this.FindControl<ListBox>("LstWhitelist");
        if (lst != null) lst.ItemsSource = filtered;

        var txtCount = this.FindControl<TextBlock>("TxtCount");
        if (txtCount != null)
            txtCount.Text = filtered.Count == _allEntriesCache.Count
                ? $"({_allEntriesCache.Count} mục)"
                : $"({filtered.Count}/{_allEntriesCache.Count} mục)";
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("TxtNewProcessName")!.Text?.Trim() ?? string.Empty;
        var hash = this.FindControl<TextBox>("TxtNewHash")!.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            ShowStatus(false, "Vui lòng nhập tên tiến trình.");
            return;
        }

        var entries = _whitelist.GetAll().Where(x => x.ProcessName != name).ToList();
        entries.Add(new WhitelistEntry
        {
            ProcessName = name,
            ExpectedSha256 = hash
        });

        SaveEntries(entries);
        ShowStatus(true, $"Đã thêm '{name}' vào Whitelist.");
        ClearForm();
        RefreshList();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string processName)
            return;

        var entries = _whitelist.GetAll().Where(x => x.ProcessName != processName).ToList();
        SaveEntries(entries);

        ShowStatus(true, $"Đã xóa '{processName}' khỏi Whitelist.");
        RefreshList();
    }

    private void OnClearFormClick(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        this.FindControl<TextBox>("TxtNewProcessName")!.Text = string.Empty;
        this.FindControl<TextBox>("TxtNewHash")!.Text = string.Empty;
    }

    private void SaveEntries(List<WhitelistEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("Data/whitelist.json", json);
    }

    // ---------- Log-only Mode ----------

    private void OnLogOnlyModeChanged(object? sender, RoutedEventArgs e)
    {
        var chk = this.FindControl<CheckBox>("ChkLogOnlyMode");
        AppServices.ExecGuard.LogOnlyMode = chk?.IsChecked == true;

        ShowStatus(true, AppServices.ExecGuard.LogOnlyMode
            ? "Đã bật Log-only Mode - mọi tiến trình vẫn chạy bình thường, chỉ ghi lại làm ứng viên."
            : "Đã tắt Log-only Mode - quay lại chế độ chặn thật.");
    }

    // ---------- Ứng viên whitelist (từ Log-only Mode) ----------

    private void RefreshSuggestions()
    {
        _allSuggestionsCache = AppServices.ExecGuardSuggestions.GetAll();
        ApplySuggestionsFilter();
    }

    private void ApplySuggestionsFilter()
    {
        var txtSearch = this.FindControl<TextBox>("TxtSearchSuggestions");
        var keyword = txtSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(keyword)
            ? _allSuggestionsCache
            : _allSuggestionsCache.Where(x =>
                x.ExePath.ToLowerInvariant().Contains(keyword) ||
                x.ProcessName.ToLowerInvariant().Contains(keyword)).ToList();

        var lst = this.FindControl<ListBox>("LstSuggestions");
        if (lst != null) lst.ItemsSource = filtered;

        var txtCount = this.FindControl<TextBlock>("TxtSuggestionCount");
        if (txtCount != null)
            txtCount.Text = filtered.Count == _allSuggestionsCache.Count
                ? $"({_allSuggestionsCache.Count} mục)"
                : $"({filtered.Count}/{_allSuggestionsCache.Count} mục)";
    }

    private void OnSearchSuggestionsChanged(object? sender, TextChangedEventArgs e) => ApplySuggestionsFilter();

    private void OnReloadSuggestionsClick(object? sender, RoutedEventArgs e)
    {
        AppServices.ExecGuardSuggestions.Load(); // Nạp lại từ file trên đĩa - phòng trường hợp ghi bởi thread ExecGuard chạy song song.
        RefreshSuggestions();
        ShowStatus(true, "Đã tải lại danh sách ứng viên.");
    }
    private void OnAddSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string exePath)
            return;

        var processName = Path.GetFileName(exePath);

        var entries = _whitelist.GetAll().Where(x => x.ProcessName != processName).ToList();
        entries.Add(new WhitelistEntry
        {
            ProcessName = processName,
            ExpectedSha256 = string.Empty
        });

        SaveEntries(entries);
        AppServices.ExecGuardSuggestions.Remove(exePath);

        ShowStatus(true, $"Đã thêm '{processName}' vào Whitelist.");
        RefreshList();
        RefreshSuggestions();
    }

    private void OnDismissSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string exePath)
            return;

        AppServices.ExecGuardSuggestions.Remove(exePath);
        RefreshSuggestions();
    }

    private void OnClearSuggestionsClick(object? sender, RoutedEventArgs e)
    {
        AppServices.ExecGuardSuggestions.ClearAll();
        RefreshSuggestions();
    }

    // ---------- Chung ----------

    private void ShowStatus(bool success, string message)
    {
        var txtStatus = this.FindControl<TextBlock>("TxtStatus");
        if (txtStatus == null) return;

        txtStatus.Text = message;
        txtStatus.Foreground = success ? Brushes.LightGreen : Brushes.OrangeRed;
    }
}