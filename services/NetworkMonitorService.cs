using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>
    /// Giám sát mạng của tiến trình: liệt kê các socket TCP/UDP đang mở và map
    /// ngược về PID sở hữu. Tương ứng chức năng 9: Giám sát mạng của tiến trình.
    ///
    /// Cách hoạt động: /proc/net/tcp (và udp, tcp6, udp6) liệt kê MỌI socket
    /// trên hệ thống kèm theo "inode" của socket đó, nhưng KHÔNG cho biết PID.
    /// Để biết socket thuộc tiến trình nào, phải quét /proc/[pid]/fd/ của từng
    /// tiến trình - mỗi fd là 1 symlink, nếu trỏ tới "socket:[inode]" thì đó
    /// chính là 1 kết nối mạng của tiến trình đó.
    /// </summary>
    public class NetworkMonitorService
    {
        private const string ProcRoot = "/proc";

        private static readonly Dictionary<string, string> TcpStateNames = new()
        {
            ["01"] = "ESTABLISHED",
            ["02"] = "SYN_SENT",
            ["03"] = "SYN_RECV",
            ["04"] = "FIN_WAIT1",
            ["05"] = "FIN_WAIT2",
            ["06"] = "TIME_WAIT",
            ["07"] = "CLOSE",
            ["08"] = "CLOSE_WAIT",
            ["09"] = "LAST_ACK",
            ["0A"] = "LISTEN",
            ["0B"] = "CLOSING"
        };

        /// <summary>Lấy toàn bộ kết nối mạng hiện có trên hệ thống (TCP/UDP, IPv4/IPv6).</summary>
        public List<NetworkConnection> GetAllConnections()
        {
            var inodeToPid = BuildInodeToPidMap();
            var result = new List<NetworkConnection>();

            result.AddRange(ParseProtocolFile("/proc/net/tcp", "TCP", inodeToPid, isTcp: true));
            result.AddRange(ParseProtocolFile("/proc/net/tcp6", "TCP6", inodeToPid, isTcp: true));
            result.AddRange(ParseProtocolFile("/proc/net/udp", "UDP", inodeToPid, isTcp: false));
            result.AddRange(ParseProtocolFile("/proc/net/udp6", "UDP6", inodeToPid, isTcp: false));

            return result;
        }

        /// <summary>
        /// Quét /proc/[pid]/fd/ của toàn bộ tiến trình để dựng bảng tra cứu
        /// inode socket -> PID sở hữu.
        /// </summary>
        private Dictionary<long, int> BuildInodeToPidMap()
        {
            var map = new Dictionary<long, int>();

            foreach (var dir in Directory.EnumerateDirectories(ProcRoot))
            {
                var folderName = Path.GetFileName(dir);
                if (!int.TryParse(folderName, out var pid))
                    continue;

                var fdDir = Path.Combine(dir, "fd");
                if (!Directory.Exists(fdDir))
                    continue;

                IEnumerable<string> fds;
                try
                {
                    fds = Directory.EnumerateFileSystemEntries(fdDir);
                }
                catch
                {
                    // Không đủ quyền đọc fd của tiến trình user khác -> bỏ qua tiến trình này.
                    continue;
                }

                foreach (var fd in fds)
                {
                    string? target;
                    try
                    {
                        target = File.ResolveLinkTarget(fd, returnFinalTarget: false)?.Name;
                    }
                    catch
                    {
                        continue;
                    }

                    // Symlink của socket có dạng "socket:[12345]".
                    if (target == null || !target.StartsWith("socket:["))
                        continue;

                    var inodeStr = target.Substring(8, target.Length - 9);
                    if (long.TryParse(inodeStr, out var inode))
                    {
                        map[inode] = pid;
                    }
                }
            }

            return map;
        }

        private List<NetworkConnection> ParseProtocolFile(
            string path, string protocolLabel, Dictionary<long, int> inodeToPid, bool isTcp)
        {
            var result = new List<NetworkConnection>();

            if (!File.Exists(path))
                return result;

            var lines = File.ReadAllLines(path);

            // Dòng đầu tiên là header (sl, local_address, rem_address...) -> bỏ qua.
            foreach (var line in lines.Skip(1))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 10)
                    continue;

                try
                {
                    var (localAddr, localPort) = ParseAddressPort(fields[1]);
                    var (remoteAddr, remotePort) = ParseAddressPort(fields[2]);
                    var stateHex = fields[3];
                    var inodeStr = fields[9];

                    var state = isTcp
                        ? TcpStateNames.GetValueOrDefault(stateHex, stateHex)
                        : string.Empty;

                    var pid = 0;
                    if (long.TryParse(inodeStr, out var inode) && inodeToPid.TryGetValue(inode, out var mappedPid))
                    {
                        pid = mappedPid;
                    }

                    result.Add(new NetworkConnection
                    {
                        Pid = pid,
                        Protocol = protocolLabel,
                        LocalAddress = localAddr,
                        LocalPort = localPort,
                        RemoteAddress = remoteAddr,
                        RemotePort = remotePort,
                        State = state
                    });
                }
                catch
                {
                    // Bỏ qua dòng lỗi định dạng bất thường, không làm gãy toàn bộ quá trình quét.
                }
            }

            return result;
        }

        private (string address, int port) ParseAddressPort(string field)
        {
            var parts = field.Split(':');
            var ip = ParseHexIpAddress(parts[0]);
            var port = Convert.ToInt32(parts[1], 16);
            return (ip.ToString(), port);
        }

        /// <summary>
        /// /proc/net/tcp lưu địa chỉ IP dạng hex, mỗi nhóm 4 byte theo thứ tự
        /// little-endian (đảo ngược byte trong từng nhóm) - cần đảo lại đúng
        /// thứ tự mới ra địa chỉ IP thật.
        /// </summary>
        private IPAddress ParseHexIpAddress(string hex)
        {
            var wordCount = hex.Length / 8;
            var bytes = new byte[hex.Length / 2];

            for (var w = 0; w < wordCount; w++)
            {
                var word = hex.Substring(w * 8, 8);
                for (var j = 0; j < 4; j++)
                {
                    var bytePair = word.Substring(j * 2, 2);
                    bytes[w * 4 + (3 - j)] = Convert.ToByte(bytePair, 16);
                }
            }

            return new IPAddress(bytes);
        }
    }
}