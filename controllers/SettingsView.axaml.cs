using System;
using System.Text;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using monitorProcess.Models;
using monitorProcess.Services;

namespace monitorProcess.controllers;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        LoadCurrentConfig();
    }

    private void LoadCurrentConfig()
    {
        var config = AppServices.Notifications.GetConfig();

        this.FindControl<CheckBox>("ChkEnableTelegram")!.IsChecked = config.EnableTelegram;
        this.FindControl<TextBox>("TxtBotToken")!.Text = config.TelegramBotToken;
        this.FindControl<TextBox>("TxtChatId")!.Text = config.TelegramChatId;

        this.FindControl<CheckBox>("ChkEnableEmail")!.IsChecked = config.EnableEmail;
        this.FindControl<TextBox>("TxtSmtpHost")!.Text = config.SmtpHost;
        this.FindControl<TextBox>("TxtSmtpPort")!.Text = config.SmtpPort.ToString();
        this.FindControl<CheckBox>("ChkSmtpSsl")!.IsChecked = config.SmtpUseSsl;
        this.FindControl<TextBox>("TxtSmtpUsername")!.Text = config.SmtpUsername;
        this.FindControl<TextBox>("TxtSmtpPassword")!.Text = config.SmtpPassword;
        this.FindControl<TextBox>("TxtFromEmail")!.Text = config.FromEmail;
        this.FindControl<TextBox>("TxtToEmail")!.Text = config.ToEmail;

         // Nạp cấu hình ngưỡng CPU/RAM.
        var threshold = AppServices.Thresholds.GetConfig();
        this.FindControl<CheckBox>("ChkEnableCpuThreshold")!.IsChecked = threshold.EnableCpuThreshold;
        this.FindControl<TextBox>("TxtCpuThreshold")!.Text = threshold.CpuThresholdPercent.ToString(CultureInfo.InvariantCulture);
        this.FindControl<CheckBox>("ChkEnableRamThreshold")!.IsChecked = threshold.EnableRamThreshold;
        this.FindControl<TextBox>("TxtRamThreshold")!.Text = threshold.RamThresholdPercent.ToString(CultureInfo.InvariantCulture);

        LoadRiskConfig();
        RefreshKnownHashesList();
    }

    private NotificationConfig ReadFormValues()
    {
        int.TryParse(this.FindControl<TextBox>("TxtSmtpPort")!.Text, out var port);

        return new NotificationConfig
        {
            EnableTelegram = this.FindControl<CheckBox>("ChkEnableTelegram")!.IsChecked == true,
            TelegramBotToken = this.FindControl<TextBox>("TxtBotToken")!.Text ?? string.Empty,
            TelegramChatId = this.FindControl<TextBox>("TxtChatId")!.Text ?? string.Empty,

            EnableEmail = this.FindControl<CheckBox>("ChkEnableEmail")!.IsChecked == true,
            SmtpHost = this.FindControl<TextBox>("TxtSmtpHost")!.Text ?? string.Empty,
            SmtpPort = port == 0 ? 587 : port,
            SmtpUseSsl = this.FindControl<CheckBox>("ChkSmtpSsl")!.IsChecked == true,
            SmtpUsername = this.FindControl<TextBox>("TxtSmtpUsername")!.Text ?? string.Empty,
            SmtpPassword = this.FindControl<TextBox>("TxtSmtpPassword")!.Text ?? string.Empty,
            FromEmail = this.FindControl<TextBox>("TxtFromEmail")!.Text ?? string.Empty,
            ToEmail = this.FindControl<TextBox>("TxtToEmail")!.Text ?? string.Empty
        };
    }


    private ThresholdConfig ReadThresholdValues(){
        double.TryParse(
            this.FindControl<TextBox>("TxtCpuThreshold")!.Text,
            NumberStyles.Any, CultureInfo.InvariantCulture, out var cpuThreshold);

        double.TryParse(
            this.FindControl<TextBox>("TxtRamThreshold")!.Text,
            NumberStyles.Any, CultureInfo.InvariantCulture, out var ramThreshold);

        return new ThresholdConfig
        {
            EnableCpuThreshold = this.FindControl<CheckBox>("ChkEnableCpuThreshold")!.IsChecked == true,
            CpuThresholdPercent = cpuThreshold == 0 ? 80 : cpuThreshold,
            EnableRamThreshold = this.FindControl<CheckBox>("ChkEnableRamThreshold")!.IsChecked == true,
            RamThresholdPercent = ramThreshold == 0 ? 50 : ramThreshold
        };
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            AppServices.Notifications.SaveConfig(ReadFormValues());
            AppServices.Thresholds.Save(ReadThresholdValues());
            ShowStatus(true, "Đã lưu cấu hình thành công.");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Lỗi khi lưu cấu hình: {ex.Message}");
        }
    }

    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        var config = ReadFormValues();
        AppServices.Notifications.SaveConfig(config); // Lưu trước để test đúng giá trị vừa nhập.

        var telegram = new TelegramNotifierService();
        var email = new EmailNotifierService();
        var testMessage = $"Tin nhắn thử nghiệm từ Process Monitor - {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

        var results = new StringBuilder();

        if (config.EnableTelegram)
        {
            var ok = await telegram.SendAsync(config, testMessage);
            results.AppendLine(ok ? "✔ Telegram: gửi thành công." : "✘ Telegram: thất bại, kiểm tra lại Token/Chat ID.");
        }

        if (config.EnableEmail)
        {
            var ok = await email.SendAsync(config, "[TEST] Process Monitor", testMessage);
            results.AppendLine(ok ? "✔ Email: gửi thành công." : "✘ Email: thất bại, kiểm tra lại SMTP.");
        }

        if (!config.EnableTelegram && !config.EnableEmail)
            results.Append("Chưa bật kênh nào để gửi thử (tick chọn Telegram hoặc Email).");

        var txtStatus = this.FindControl<TextBlock>("TxtSettingsStatus");
        if (txtStatus != null)
        {
            txtStatus.Text = results.ToString();
            txtStatus.Foreground = Brushes.LightGray;
        }
    }

    private void ShowStatus(bool success, string message)
    {
        var txtStatus = this.FindControl<TextBlock>("TxtSettingsStatus");
        if (txtStatus == null) return;

        txtStatus.Text = message;
        txtStatus.Foreground = success ? Brushes.LightGreen : Brushes.OrangeRed;
    }

    private void LoadRiskConfig()
    {
        var r = AppServices.RiskScoring.GetConfig();

        this.FindControl<CheckBox>("ChkRiskEnabled")!.IsChecked = r.Enabled;
        this.FindControl<TextBox>("TxtScoreTmp")!.Text = r.ScoreRunningFromTmp.ToString();
        this.FindControl<TextBox>("TxtScoreNoHash")!.Text = r.ScoreHashNotInDatabase.ToString();
        this.FindControl<TextBox>("TxtScoreCpu")!.Text = r.ScoreHighCpu.ToString();
        this.FindControl<TextBox>("TxtRiskCpuThreshold")!.Text = r.CpuThresholdForScoring.ToString(CultureInfo.InvariantCulture);
        this.FindControl<TextBox>("TxtScorePort")!.Text = r.ScoreUnusualPort.ToString();
        this.FindControl<TextBox>("TxtScoreChanged")!.Text = r.ScoreExecutableChanged.ToString();
        this.FindControl<TextBox>("TxtDangerThreshold")!.Text = r.DangerousThresholdPercent.ToString(CultureInfo.InvariantCulture);
        this.FindControl<TextBox>("TxtTrustedPorts")!.Text = string.Join(",", r.TrustedPorts);
    }

    private void OnSaveRiskConfigClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            int.TryParse(this.FindControl<TextBox>("TxtScoreTmp")!.Text, out var scoreTmp);
            int.TryParse(this.FindControl<TextBox>("TxtScoreNoHash")!.Text, out var scoreNoHash);
            int.TryParse(this.FindControl<TextBox>("TxtScoreCpu")!.Text, out var scoreCpu);
            double.TryParse(this.FindControl<TextBox>("TxtRiskCpuThreshold")!.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var cpuThreshold);
            int.TryParse(this.FindControl<TextBox>("TxtScorePort")!.Text, out var scorePort);
            int.TryParse(this.FindControl<TextBox>("TxtScoreChanged")!.Text, out var scoreChanged);
            double.TryParse(this.FindControl<TextBox>("TxtDangerThreshold")!.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dangerThreshold);

            var portsText = this.FindControl<TextBox>("TxtTrustedPorts")!.Text ?? string.Empty;
            var trustedPorts = portsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : -1)
                .Where(v => v > 0)
                .ToList();

            var config = new RiskScoringConfig
            {
                Enabled = this.FindControl<CheckBox>("ChkRiskEnabled")!.IsChecked == true,
                ScoreRunningFromTmp = scoreTmp == 0 ? 30 : scoreTmp,
                EnableHashDatabaseCheck = true,
                ScoreHashNotInDatabase = scoreNoHash == 0 ? 20 : scoreNoHash,
                ScoreExecutableChanged = scoreChanged == 0 ? 30 : scoreChanged,
                EnableCpuCheck = true,
                ScoreHighCpu = scoreCpu == 0 ? 15 : scoreCpu,
                CpuThresholdForScoring = cpuThreshold == 0 ? 90 : cpuThreshold,
                EnablePortCheck = true,
                ScoreUnusualPort = scorePort == 0 ? 20 : scorePort,
                TrustedPorts = trustedPorts.Count > 0 ? trustedPorts : new List<int> { 22, 80, 443 },
                DangerousThresholdPercent = dangerThreshold == 0 ? 50 : dangerThreshold
            };

            AppServices.RiskScoring.SaveConfig(config);
            ShowStatus(true, "Đã lưu cấu hình Risk Scoring.");
        }
        catch (Exception ex)
        {
            ShowStatus(false, $"Lỗi khi lưu cấu hình Risk Scoring: {ex.Message}");
        }
    }

    private void RefreshKnownHashesList()
    {
        var lst = this.FindControl<ListBox>("LstKnownHashes");
        if (lst != null) lst.ItemsSource = AppServices.RiskScoring.GetAllKnownHashes();
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
            this.FindControl<TextBox>("TxtNewKnownPath")!.Text = string.Empty;
            this.FindControl<TextBox>("TxtNewKnownDesc")!.Text = string.Empty;
            RefreshKnownHashesList();
        }
    }

    private void OnRemoveKnownHashClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string exePath) return;

        AppServices.RiskScoring.RemoveKnownFile(exePath);
        ShowStatus(true, $"Đã xóa {exePath} khỏi CSDL đáng tin cậy.");
        RefreshKnownHashesList();
    }
}