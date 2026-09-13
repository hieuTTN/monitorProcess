namespace monitorProcess.Models
{
    /// <summary>
    /// Cấu hình gửi cảnh báo qua Telegram Bot / Email.
    /// Tương ứng chức năng 10: Cảnh báo qua Email/Telegram Bot.
    /// </summary>
    public class NotificationConfig
    {
        public bool EnableTelegram { get; set; }
        public string TelegramBotToken { get; set; } = string.Empty;
        public string TelegramChatId { get; set; } = string.Empty;

        public bool EnableEmail { get; set; }
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;
        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string ToEmail { get; set; } = string.Empty;
    }
}