using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>Gửi tin nhắn cảnh báo qua Telegram Bot API.</summary>
    public class TelegramNotifierService
    {
        // Dùng chung 1 HttpClient tĩnh cho cả class - tránh tạo mới liên tục
        // gây cạn kiệt socket (best practice tiêu chuẩn khi dùng HttpClient).
        private static readonly HttpClient _httpClient = new();

        public async Task<bool> SendAsync(NotificationConfig config, string message)
        {
            if (!config.EnableTelegram ||
                string.IsNullOrEmpty(config.TelegramBotToken) ||
                string.IsNullOrEmpty(config.TelegramChatId))
                return false;

            var url = $"https://api.telegram.org/bot{config.TelegramBotToken}/sendMessage";

            var payload = new
            {
                chat_id = config.TelegramChatId,
                text = message
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Lỗi mạng/token sai... không throw ra ngoài, chỉ báo thất bại.
                return false;
            }
        }
    }
}