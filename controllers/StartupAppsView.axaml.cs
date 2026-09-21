using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using monitorProcess.Models;
using monitorProcess.Services;

namespace monitorProcess.controllers;

public partial class StartupAppsView : UserControl
{
    private List<StartupItem> _allItemsCache = new();

    public StartupAppsView()
    {
        InitializeComponent();
        RefreshList();
    }

    private void RefreshList()
    {
        _allItemsCache = AppServices.StartupApps.GetStartupItems();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var txtSearch = this.FindControl<TextBox>("TxtSearch");
        var keyword = txtSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(keyword)
            ? _allItemsCache
            : _allItemsCache.Where(x =>
                x.Name.ToLowerInvariant().Contains(keyword) ||
                x.Command.ToLowerInvariant().Contains(keyword) ||
                x.Description.ToLowerInvariant().Contains(keyword)).ToList();

        var lst = this.FindControl<ListBox>("LstStartupItems");
        if (lst != null) lst.ItemsSource = filtered;

        var txtCount = this.FindControl<TextBlock>("TxtCount");
        if (txtCount != null)
            txtCount.Text = filtered.Count == _allItemsCache.Count
                ? $"({_allItemsCache.Count} mục)"
                : $"({filtered.Count}/{_allItemsCache.Count} mục)";
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnRefreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RefreshList();

    private void OnToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StartupItem item)
            return;

        var (success, message) = AppServices.StartupApps.ToggleEnabled(item, !item.Enabled);
        ShowStatus(success, message);

        if (success)
            RefreshList();
    }

    private void ShowStatus(bool success, string message)
    {
        var txtStatus = this.FindControl<TextBlock>("TxtStatus");
        if (txtStatus == null) return;

        txtStatus.Text = message;
        txtStatus.Foreground = success ? Avalonia.Media.Brushes.LightGreen : Avalonia.Media.Brushes.OrangeRed;
    }
}