using System;
using System.Collections.Generic;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Phát hiện tiến trình có file thực thi nằm trong các thư mục tạm thường bị
    /// lợi dụng để chạy mã độc (vì các thư mục này thường có quyền ghi cho mọi
    /// user, và không phải nơi phần mềm hợp lệ thường cài đặt vào).
    /// Tương ứng chức năng 4: Cảnh báo tiến trình chạy từ thư mục tạm nguy hiểm.
    /// </summary>
    public class PathAnomalyDetectionService
    {
        // Danh sách thư mục coi là "nguy hiểm" nếu file thực thi nằm trong đó.
        private static readonly string[] DangerousPrefixes =
        {
            "/tmp",
            "/var/tmp",
            "/dev/shm"
        };

        /// <summary>
        /// Kiểm tra 1 tiến trình, trả về AlertLog nếu phát hiện bất thường,
        /// hoặc null nếu tiến trình bình thường.
        /// </summary>
        public AlertLog? Check(ProcessInfo process)
        {
            if (string.IsNullOrEmpty(process.ExePath))
                return null;

            foreach (var prefix in DangerousPrefixes)
            {
                if (process.ExePath.StartsWith(prefix + "/", StringComparison.Ordinal)
                    || process.ExePath == prefix)
                {
                    return new AlertLog
                    {
                        Pid = process.Pid,
                        ProcessName = process.Name,
                        ExePath = process.ExePath,
                        UserName = process.UserName,
                        Type = AlertType.DangerousPath,
                        Severity = AlertSeverity.Warning,
                        Message = $"Tiến trình '{process.Name}' (PID {process.Pid}) đang chạy từ " +
                                  $"thư mục tạm nguy hiểm: {process.ExePath}"
                    };
                }
            }

            return null;
        }

        /// <summary>Kiểm tra hàng loạt tiến trình, chỉ trả về các cảnh báo phát sinh.</summary>
        public List<AlertLog> CheckAll(IEnumerable<ProcessInfo> processes)
        {
            var alerts = new List<AlertLog>();
            foreach (var p in processes)
            {
                var alert = Check(p);
                if (alert != null)
                    alerts.Add(alert);
            }
            return alerts;
        }
    }
}