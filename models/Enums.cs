namespace monitorProcess.Models
{
    /// <summary>
    /// Trạng thái của tiến trình, lấy từ ký tự State trong /proc/[pid]/status
    /// (R=Running, S=Sleeping, D=Uninterruptible Sleep, Z=Zombie, T=Stopped...)
    /// </summary>
    public enum ProcessState
    {
        Running,
        Sleeping,
        UninterruptibleSleep,
        Stopped,
        Zombie,
        Idle,
        Unknown
    }

    /// <summary>
    /// Phân loại các dạng cảnh báo mà hệ thống có thể phát hiện được.
    /// </summary>
    public enum AlertType
    {
        DangerousPath,          // Chạy từ /tmp, /var/tmp, /dev/shm
        HashMismatch,           // Hash file thực thi không khớp / không nằm trong whitelist
        NotInWhitelist,         // Tiến trình không thuộc danh sách được phép chạy
        MatchDangerousDb,       // Trùng tên/hash với CSDL tiến trình nguy hiểm
        HighCpuUsage,           // CPU đột biến
        HighRamUsage,           // RAM đột biến
        SuspiciousParent,       // Chạy quyền root nhưng cha là user thường
        ImpersonatingSystemProc // Mạo danh tiến trình hệ thống (systemd, sshd...)
    }

    /// <summary>
    /// Mức độ nghiêm trọng của cảnh báo, dùng để quyết định cách xử lý
    /// (ví dụ: Critical thì tự động kill, Warning thì chỉ ghi log).
    /// </summary>
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>
    /// Hành động đã/sẽ thực hiện với tiến trình bị cảnh báo.
    /// </summary>
    public enum ActionTaken
    {
        None,
        Notified,
        Stopped,      // SIGSTOP
        Resumed,      // SIGCONT
        Killed        // SIGKILL
    }
}