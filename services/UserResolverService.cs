using System;
using System.Collections.Generic;
using System.IO;

namespace monitorProcess.Services
{
    /// <summary>
    /// Đọc /etc/passwd một lần và cache lại để tra cứu UID -> username nhanh,
    /// tránh phải đọc file này lặp lại mỗi lần liệt kê tiến trình.
    /// </summary>
    public class UserResolverService
    {
        private readonly Dictionary<int, string> _uidToName = new Dictionary<int, string>();
        private DateTime _lastLoaded = DateTime.MinValue;

        // /etc/passwd hiếm khi đổi lúc chương trình đang chạy, nhưng vẫn nạp lại
        // định kỳ để không bị "cứng" dữ liệu nếu có thêm user mới trong lúc chạy.
        private static readonly TimeSpan ReloadInterval = TimeSpan.FromMinutes(5);

        public string Resolve(int uid)
        {
            EnsureLoaded();
            return _uidToName.TryGetValue(uid, out var name) ? name : uid.ToString();
        }

        private void EnsureLoaded()
        {
            if (DateTime.Now - _lastLoaded < ReloadInterval && _uidToName.Count > 0)
                return;

            _uidToName.Clear();

            const string passwdPath = "/etc/passwd";
            if (!File.Exists(passwdPath))
            {
                _lastLoaded = DateTime.Now;
                return;
            }

            try
            {
                // Định dạng mỗi dòng: username:x:uid:gid:comment:home:shell
                foreach (var line in File.ReadLines(passwdPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split(':');
                    if (parts.Length < 3)
                        continue;

                    if (int.TryParse(parts[2], out var uid))
                    {
                        _uidToName[uid] = parts[0];
                    }
                }
            }
            catch (IOException)
            {
                // Bỏ qua nếu không đọc được, sẽ fallback hiển thị UID dạng số.
            }

            _lastLoaded = DateTime.Now;
        }
    }
}