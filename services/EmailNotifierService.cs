using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>Gửi email cảnh báo qua SMTP.</summary>
    public class EmailNotifierService
    {
        public async Task<bool> SendAsync(NotificationConfig config, string subject, string body)
        {
            if (!config.EnableEmail ||
                string.IsNullOrEmpty(config.SmtpHost) ||
                string.IsNullOrEmpty(config.FromEmail) ||
                string.IsNullOrEmpty(config.ToEmail))
                return false;

            try
            {
                using var client = new SmtpClient(config.SmtpHost, config.SmtpPort)
                {
                    EnableSsl = config.SmtpUseSsl,
                    Credentials = new NetworkCredential(config.SmtpUsername, config.SmtpPassword)
                };

                using var message = new MailMessage(config.FromEmail, config.ToEmail, subject, body);

                await client.SendMailAsync(message);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}