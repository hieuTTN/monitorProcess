using System.IO;
using System.Security.Cryptography;

namespace monitorProcess.Services
{
    /// <summary>
    /// Tính mã băm SHA-256 của file thực thi để kiểm tra tính toàn vẹn.
    /// Tương ứng chức năng 3: Kiểm tra tính toàn vẹn file thực thi.
    /// </summary>
    public class HashService
    {
        /// <summary>
        /// Tính SHA-256 của file tại đường dẫn cho trước, trả về chuỗi hex viết
        /// thường (ví dụ "a3f5..."). Trả về null nếu không đọc được file (file
        /// không tồn tại, tiến trình đã kết thúc, hoặc không đủ quyền).
        /// </summary>
        public string? ComputeSha256(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hashBytes = sha256.ComputeHash(stream);
                return System.Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (IOException)
            {
                return null;
            }
            catch (System.UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}