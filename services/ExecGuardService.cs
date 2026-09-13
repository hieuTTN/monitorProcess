using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace monitorProcess.Services
{
    /// <summary>
    /// Chặn tiến trình lạ NGAY TỪ ĐẦU - TRƯỚC KHI nó được thực thi. Khác hẳn
    /// với cơ chế cũ trong ProcessListView (quét /proc mỗi 3s rồi kill PID
    /// mới xuất hiện): cách cũ CHỈ có thể "phát hiện sau khi đã chạy", vì khi
    /// PID xuất hiện trong /proc nghĩa là kernel đã fork+exec xong rồi.
    ///
    /// Cơ chế ở đây dùng API "fanotify" của kernel Linux với sự kiện đặc biệt
    /// FAN_OPEN_EXEC_PERM ("permission event"): mỗi khi có ai chạy 1 file thực
    /// thi, kernel sẽ TẠM DỪNG việc exec đó lại, gửi 1 yêu cầu xin phép sang
    /// app này; app tra whitelist rồi trả lời FAN_ALLOW hoặc FAN_DENY - kernel
    /// CHỈ tiếp tục cho exec nếu nhận được ALLOW. Nếu DENY, file không bao giờ
    /// được nạp để chạy - không có PID nào được sinh ra cả, không phải "sinh
    /// ra rồi giết".
    ///
    /// YÊU CẦU BẮT BUỘC ĐỂ CHẠY ĐƯỢC:
    ///  1) Chỉ chạy trên Linux.
    ///  2) Phải có quyền root, HOẶC cấp quyền CAP_SYS_ADMIN cho file thực thi
    ///     bằng lệnh (chạy 1 lần, cần sudo):
    ///         sudo setcap cap_sys_admin+ep /duong/dan/toi/file/thuc/thi/monitorProcess
    ///     Nếu thiếu quyền, Start() sẽ tự phát hiện, in cảnh báo ra console,
    ///     và TẮT tính năng này - các tính năng khác của app (liệt kê, cảnh
    ///     báo, kill sau khi phát hiện...) vẫn hoạt động bình thường.
    ///
    /// LƯU Ý QUAN TRỌNG (nên ghi vào báo cáo đề tài):
    ///  - Đây là "permission event": kernel TẠM DỪNG hẳn lệnh exec() lại cho
    ///    tới khi nhận được phản hồi ALLOW/DENY. Vì FAN_MARK_FILESYSTEM đang
    ///    theo dõi TOÀN BỘ hệ thống, số lượng sự kiện sinh ra trong 1 giây có
    ///    thể rất lớn (mọi tiến trình nền của desktop, cron, service...). Nếu
    ///    xử lý TUẦN TỰ (1 sự kiện 1 lúc trên 1 thread), các sự kiện sẽ xếp
    ///    hàng chờ và có thể KHÔNG BAO GIỜ tới lượt được trả lời, khiến toàn
    ///    bộ tiến trình xin exec bị "treo" vô thời hạn - kể cả tiến trình đã
    ///    có trong whitelist đáng lẽ phải được ALLOW ngay. Vì vậy mỗi sự kiện
    ///    ở đây được xử lý trên 1 Task RIÊNG (chạy song song qua thread pool)
    ///    thay vì tuần tự, và luôn có khối try/finally đảm bảo BẮT BUỘC phải
    ///    ghi phản hồi dù có lỗi xảy ra ở bước nào - đây là đánh đổi giữa "an
    ///    toàn" (fail-closed, mặc định chặn khi lỗi) và "không làm sập hệ
    ///    thống" (fail-open, mặc định cho qua khi lỗi); code hiện tại chọn
    ///    fail-open cho trường hợp lỗi ngoài ý muốn, cần nêu rõ đánh đổi này
    ///    khi làm báo cáo/luận văn.
    /// </summary>
    public class ExecGuardService
    {
        // ===== Hằng số lấy từ header kernel Linux <sys/fanotify.h> / <fcntl.h> =====
        private const uint FAN_CLASS_CONTENT = 0x00000004;
        private const uint FAN_OPEN_EXEC_PERM = 0x00040000;
        private const uint FAN_MARK_ADD = 0x00000001;
        private const uint FAN_MARK_FILESYSTEM = 0x00000100;
        private const uint FAN_ALLOW = 0x01;
        private const uint FAN_DENY = 0x02;
        private const int AT_FDCWD = -100;
        private const int O_RDONLY = 0x00000000;

        // sizeof(struct fanotify_event_metadata) trên kernel Linux = 24 byte cố định.
        private const int EVENT_META_SIZE = 24;

        [DllImport("libc", SetLastError = true)]
        private static extern int fanotify_init(uint flags, uint event_f_flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int fanotify_mark(int fanotify_fd, uint flags, ulong mask, int dirfd, string pathname);

        [DllImport("libc", SetLastError = true)]
        private static extern int read(int fd, byte[] buf, int count);

        [DllImport("libc", SetLastError = true)]
        private static extern int write(int fd, byte[] buf, int count);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        private readonly WhitelistService _whitelist;
        private int _fanFd = -1;
        private Thread? _worker;
        private volatile bool _running;

        /// <summary>Tổng số lần đã CHẶN (deny) 1 tiến trình lạ kể từ lúc Start().</summary>
        public int DeniedCount { get; private set; }

        /// <summary>Gán từ bên ngoài (vd ProcessListView) để hiện log DENY lên UI/console.</summary>
        public Action<string>? OnDenied { get; set; }

        /// <summary>true nếu tính năng đang thực sự hoạt động (đã fanotify_init + mark thành công).</summary>
        public bool IsActive => _running;

        private readonly ExecGuardSuggestionService _suggestions;

        /// <summary>
        /// Chế độ Log-only: KHÔNG chặn thật (luôn ALLOW), chỉ ghi lại những gì "lẽ ra
        /// đã bị chặn" vào ExecGuardSuggestionService để xem lại sau. Dùng khi mới
        /// setup whitelist, chưa chắc đã đủ đầy đủ - tránh làm treo/hỏng hệ thống
        /// trong lúc đang dò tên tiến trình.
        /// </summary>
        public bool LogOnlyMode { get; set; } = false;

        public ExecGuardService(WhitelistService whitelist, ExecGuardSuggestionService suggestions)
        {
            _whitelist = whitelist;
            _suggestions = suggestions;
        }

        public ExecGuardService(WhitelistService whitelist)
        {
            _whitelist = whitelist;
        }

        private const string KillSwitchPath = "/etc/monitorProcess/execguard.disabled";

        /// <summary>Bật chặn thực thi từ đầu. Gọi 1 LẦN DUY NHẤT lúc app khởi động.</summary>
        public void Start()
        {
            if (!OperatingSystem.IsLinux())
            {
                Console.WriteLine("[ExecGuard] Chỉ hỗ trợ Linux - bỏ qua, không bật tính năng này.");
                return;
            }

            if (File.Exists(KillSwitchPath))
            {
                Console.WriteLine(
                    $"[ExecGuard] Phát hiện file van an toàn '{KillSwitchPath}' - " +
                    "TẮT tính năng chặn-từ-đầu theo yêu cầu. Xoá file này để bật lại.");
                return;
            }

            _fanFd = fanotify_init(FAN_CLASS_CONTENT, O_RDONLY);
            if (_fanFd < 0)
            {
                Console.WriteLine(
                    "[ExecGuard] Không khởi tạo được fanotify (thường do THIẾU QUYỀN ROOT). " +
                    "Hãy chạy app bằng 'sudo dotnet run' (lúc code), hoặc với bản build thật: " +
                    "sudo setcap cap_sys_admin+ep <đường dẫn file thực thi>. " +
                    "Tính năng 'chặn-từ-đầu' đang TẮT - phần còn lại của app vẫn chạy bình thường.");
                return;
            }

            var markResult = fanotify_mark(
                _fanFd,
                FAN_MARK_ADD | FAN_MARK_FILESYSTEM,
                FAN_OPEN_EXEC_PERM,
                AT_FDCWD,
                "/"); // đánh dấu giám sát trên TOÀN BỘ filesystem gốc

            if (markResult < 0)
            {
                Console.WriteLine("[ExecGuard] fanotify_mark thất bại - tắt tính năng chặn-từ-đầu.");
                close(_fanFd);
                _fanFd = -1;
                return;
            }

            _running = true;
            _worker = new Thread(WatchLoop) { IsBackground = true, Name = "ExecGuard" };
            _worker.Start();
            Console.WriteLine("[ExecGuard] Đã BẬT chặn thực thi từ đầu (fanotify FAN_OPEN_EXEC_PERM).");
        }

        /// <summary>Tắt tính năng - gọi khi đóng app (không bắt buộc nếu app luôn tắt bằng cách kill toàn bộ).</summary>
        public void Stop()
        {
            _running = false;
            if (_fanFd >= 0)
            {
                close(_fanFd); // làm read() đang chờ (blocking) trả về lỗi -> vòng lặp tự thoát
                _fanFd = -1;
            }
        }

        private void WatchLoop()
        {
            var buffer = new byte[4096];

            while (_running)
            {
                int bytesRead = read(_fanFd, buffer, buffer.Length);
                if (bytesRead <= 0)
                    break; // fd đã bị đóng (Stop()) hoặc có lỗi -> dừng hẳn thread

                // Copy riêng dữ liệu của lượt đọc này ra 1 mảng mới trước khi
                // giao cho các Task xử lý song song - buffer gốc sẽ bị ghi đè
                // ngay ở vòng lặp tiếp theo (read() tiếp theo), nếu không copy
                // các Task chạy sau có thể đọc nhầm dữ liệu của lượt đọc mới.
                var localBuffer = new byte[bytesRead];
                Array.Copy(buffer, localBuffer, bytesRead);

                int offset = 0;
                while (offset + EVENT_META_SIZE <= bytesRead)
                {
                    int eventLen = BitConverter.ToInt32(localBuffer, offset + 0);
                    ulong mask = BitConverter.ToUInt64(localBuffer, offset + 8);
                    int eventFd = BitConverter.ToInt32(localBuffer, offset + 16);
                    int eventPid = BitConverter.ToInt32(localBuffer, offset + 20);

                    if (eventFd >= 0 && (mask & FAN_OPEN_EXEC_PERM) != 0)
                    {
                        // QUAN TRỌNG: xử lý mỗi sự kiện trên 1 Task RIÊNG, chạy
                        // song song trên thread pool - để 1 sự kiện xử lý chậm
                        // (vd phải tính hash file lớn) KHÔNG chặn các sự kiện
                        // khác đang chờ trả lời. Nếu xử lý tuần tự trên đúng 1
                        // thread như trước đây, khi hệ thống có nhiều tiến
                        // trình cùng exec() gần như đồng thời (rất phổ biến
                        // với desktop GNOME/cron/service nền), hàng đợi sẽ
                        // không bao giờ giảm -> mọi lệnh exec() bị "treo" vô
                        // thời hạn dù whitelist hoàn toàn đúng.
                        Task.Run(() => HandleExecRequest(eventFd, eventPid));
                    }

                    offset += eventLen > 0 ? eventLen : EVENT_META_SIZE;
                }
            }
        }

        /// <summary>
        /// Xử lý 1 yêu cầu "xin phép chạy": xác định file nào đang xin exec, tra
        /// whitelist, rồi PHẢI trả lời ALLOW/DENY - đây là bước bắt buộc, nếu
        /// không trả lời thì tiến trình xin chạy sẽ bị treo vô thời hạn. Toàn bộ
        /// thân hàm được bọc try/finally để đảm bảo LUÔN LUÔN ghi được phản hồi,
        /// dù có exception xảy ra ở bất kỳ bước nào bên trong.
        /// </summary>
        private void HandleExecRequest(int eventFd, int pid)
        {
            bool allowed = false;

            try
            {
                // "/proc/self/fd/{eventFd}" - "self" ở đây là CHÍNH APP GIÁM
                // SÁT NÀY, vì kernel đã nhân bản (dup) fd đó sang cho app,
                // không phải fd của tiến trình đang xin chạy.
                string exePath = "";
                try
                {
                    exePath = File.ResolveLinkTarget($"/proc/self/fd/{eventFd}", true)?.FullName ?? "";
                }
                catch
                {
                    // Không đọc được đường dẫn -> coi như không xác định được,
                    // IsExecutablePathAllowed với chuỗi rỗng sẽ tự trả về false.
                }

                allowed = _whitelist.IsExecutablePathAllowed(exePath);

                if (!allowed)
                {
                    if (LogOnlyMode)
                    {
                        // Log-only: ghi lại làm ứng viên, nhưng vẫn CHO PHÉP chạy thật -
                        // không làm gián đoạn công việc trong lúc đang dò danh sách.
                        _suggestions.RecordSeen(exePath);
                        allowed = true;

                        Console.WriteLine(
                            $"[ExecGuard] (Log-only) Lẽ ra đã chặn PID {pid}: '{exePath}' - đã ghi làm ứng viên whitelist.");
                    }
                    else
                    {
                        DeniedCount++;
                        var msg = $"[ExecGuard] ĐÃ CHẶN TỪ ĐẦU: PID {pid} muốn chạy '{exePath}' " +
                                "- không nằm trong whitelist, không cho phép thực thi.";
                        Console.WriteLine(msg);
                        OnDenied?.Invoke(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                // Có lỗi bất ngờ khi kiểm tra whitelist (vd IOException lúc
                // tính hash) -> ƯU TIÊN CHO PHÉP CHẠY (fail-open) thay vì để
                // tiến trình treo vô thời hạn, vì "treo cả hệ thống" thường
                // nguy hiểm hơn nhiều so với 1 lần bỏ sót kiểm tra. Đây là
                // đánh đổi an toàn/ổn định đã ghi chú ở đầu file.
                Console.WriteLine(
                    $"[ExecGuard] Lỗi khi xử lý sự kiện PID {pid}: {ex.Message} - mặc định CHO PHÉP chạy.");
                allowed = true;
            }
            finally
            {
                // LUÔN LUÔN ghi phản hồi, dù có lỗi ở bước nào phía trên - đây
                // là điều kiện BẮT BUỘC để tiến trình xin exec không bị treo
                // vĩnh viễn chờ kernel.
                try
                {
                    var response = new byte[8];
                    BitConverter.GetBytes(eventFd).CopyTo(response, 0);
                    BitConverter.GetBytes(allowed ? FAN_ALLOW : FAN_DENY).CopyTo(response, 4);
                    write(_fanFd, response, response.Length);
                }
                catch
                {
                    // _fanFd có thể đã bị đóng (app đang Stop()) - bỏ qua, không
                    // còn ý nghĩa để trả lời nữa vì fanotify đã tắt.
                }

                close(eventFd); // fd này kernel dup riêng cho sự kiện - PHẢI tự đóng, không thì rò rỉ fd.
            }
        }
    }
}