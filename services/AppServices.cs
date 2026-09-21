namespace monitorProcess.Services
{
    /// <summary>
    /// Nơi giữ các service cần dùng CHUNG giữa nhiều trang UI (singleton),
    /// tránh mỗi trang tự khởi tạo 1 bản riêng - ví dụ nếu ProcessListView và
    /// SettingsView mỗi bên tự new AlertNotificationService() thì khi bạn lưu
    /// cấu hình ở Settings, ProcessListView vẫn dùng cấu hình cũ đã nạp từ lúc
    /// khởi động, không cập nhật ngay được.
    /// </summary>
    public static class AppServices
    {
        public static AlertNotificationService Notifications { get; } =
            new AlertNotificationService("Data/notification_config.json");

        public static ResourceThresholdDetectionService Thresholds { get; } =
            new ResourceThresholdDetectionService("Data/threshold_config.json");

        public static RiskScoringService RiskScoring { get; } =
            new RiskScoringService("Data/risk_scoring_config.json", "Data/known_hashes.json", new HashService());

        public static WhitelistService Whitelist { get; } 
        = new WhitelistService("Data/whitelist.json", new HashService());

        public static ExecGuardSuggestionService ExecGuardSuggestions { get; } =
            new ExecGuardSuggestionService("Data/execguard_suggestions.json");
        
        public static ExecGuardService ExecGuard { get; } = new ExecGuardService(Whitelist, ExecGuardSuggestions);

        public static StartupAppsService StartupApps { get; } = new StartupAppsService();
    }
}