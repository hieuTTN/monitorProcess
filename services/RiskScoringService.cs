using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using monitorProcess.Models;

namespace monitorProcess.Services
{
    /// <summary>Kết quả chấm điểm rủi ro cho 1 tiến trình.</summary>
    public class RiskScoreResult
    {
        public int Score { get; set; }
        public bool IsDangerous { get; set; }
        public List<string> Reasons { get; set; } = new();
    }

    /// <summary>
    /// Chấm điểm rủi ro theo 5 tiêu chí trọng số (đã bàn theo ảnh):
    /// Chạy từ /tmp, Không có hash trong DB, CPU cao, Mở port lạ, File thay đổi.
    /// Tổng điểm giới hạn tối đa 100; vượt ngưỡng % cấu hình -> coi là nguy hiểm.
    /// </summary>
    public class RiskScoringService
    {
        private readonly string _configPath;
        private readonly string _hashDbPath;
        private readonly HashService _hashService;
        private readonly PathAnomalyDetectionService _pathChecker;

        private RiskScoringConfig _config = new();
        private Dictionary<string, KnownHashEntry> _knownHashes = new();

        public RiskScoringService(string configPath, string hashDbPath, HashService hashService)
        {
            _configPath = configPath;
            _hashDbPath = hashDbPath;
            _hashService = hashService;
            _pathChecker = new PathAnomalyDetectionService();

            LoadConfig();
            LoadHashDb();
        }

        // ---------- Cấu hình ----------

        public void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                _config = new RiskScoringConfig();
                return;
            }

            var json = File.ReadAllText(_configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _config = JsonSerializer.Deserialize<RiskScoringConfig>(json, options) ?? new RiskScoringConfig();
        }

        public void SaveConfig(RiskScoringConfig config)
        {
            _config = config;
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(config, options));
        }

        public RiskScoringConfig GetConfig() => _config;

        // ---------- CSDL hash đáng tin cậy ----------

        public void LoadHashDb()
        {
            if (!File.Exists(_hashDbPath))
            {
                _knownHashes = new Dictionary<string, KnownHashEntry>();
                return;
            }

            var json = File.ReadAllText(_hashDbPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var entries = JsonSerializer.Deserialize<List<KnownHashEntry>>(json, options) ?? new List<KnownHashEntry>();
            _knownHashes = entries.ToDictionary(e => e.ExePath, e => e);
        }

        public IReadOnlyList<KnownHashEntry> GetAllKnownHashes() => _knownHashes.Values.ToList();

        /// <summary>Thêm 1 file vào CSDL đáng tin cậy, tự tính hash hiện tại làm baseline.</summary>
        public (bool success, string message) AddKnownFile(string exePath, string description)
        {
            if (!File.Exists(exePath))
                return (false, $"File không tồn tại: {exePath}");

            var hash = _hashService.ComputeSha256(exePath);
            if (hash == null)
                return (false, $"Không đọc được file để tính hash: {exePath}");

            var entries = _knownHashes.Values.Where(e => e.ExePath != exePath).ToList();
            entries.Add(new KnownHashEntry { ExePath = exePath, Sha256 = hash, Description = description });
            SaveHashDb(entries);

            return (true, $"Đã thêm {exePath} vào CSDL đáng tin cậy (hash: {hash[..12]}...)");
        }

        public void RemoveKnownFile(string exePath)
        {
            var entries = _knownHashes.Values.Where(e => e.ExePath != exePath).ToList();
            SaveHashDb(entries);
        }

        private void SaveHashDb(List<KnownHashEntry> entries)
        {
            var directory = Path.GetDirectoryName(_hashDbPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_hashDbPath, JsonSerializer.Serialize(entries, options));
            LoadHashDb();
        }

        // ---------- Chấm điểm ----------

        /// <summary>
        /// Chấm điểm 1 tiến trình theo 5 tiêu chí. listenPorts là danh sách các
        /// cổng đang ở trạng thái LISTEN mà tiến trình này đang mở.
        /// </summary>
        public RiskScoreResult Check(ProcessInfo process, double cpuPercent, List<int> listenPorts)
        {
            var result = new RiskScoreResult();

            if (!_config.Enabled)
                return result;

            int score = 0;

            // 1. Chạy từ /tmp, /var/tmp, /dev/shm.
            if (_pathChecker.Check(process) != null)
            {
                score += _config.ScoreRunningFromTmp;
                result.Reasons.Add($"Chạy từ thư mục tạm (+{_config.ScoreRunningFromTmp})");
            }

            // 2 + 5. Kiểm tra hash - dùng chung 1 lần tính, chia 2 nhánh loại trừ nhau.
            if ((_config.EnableHashDatabaseCheck) && !string.IsNullOrEmpty(process.ExePath))
            {
                if (_knownHashes.TryGetValue(process.ExePath, out var known))
                {
                    var currentHash = _hashService.ComputeSha256(process.ExePath);
                    if (currentHash != null &&
                        !string.Equals(currentHash, known.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        score += _config.ScoreExecutableChanged;
                        result.Reasons.Add($"File thực thi đã bị thay đổi so với baseline (+{_config.ScoreExecutableChanged})");
                    }
                }
                else
                {
                    score += _config.ScoreHashNotInDatabase;
                    result.Reasons.Add($"Không có hash trong CSDL đáng tin cậy (+{_config.ScoreHashNotInDatabase})");
                }
            }

            // 3. CPU cao bất thường.
            if (_config.EnableCpuCheck && cpuPercent >= _config.CpuThresholdForScoring)
            {
                score += _config.ScoreHighCpu;
                result.Reasons.Add($"CPU {cpuPercent:0.0}% vượt {_config.CpuThresholdForScoring:0}% (+{_config.ScoreHighCpu})");
            }

            // 4. Mở port lạ (không nằm trong danh sách port quen thuộc).
            if (_config.EnablePortCheck && listenPorts.Any(p => !_config.TrustedPorts.Contains(p)))
            {
                var unusual = listenPorts.Where(p => !_config.TrustedPorts.Contains(p)).Distinct();
                score += _config.ScoreUnusualPort;
                result.Reasons.Add($"Mở port lạ: {string.Join(", ", unusual)} (+{_config.ScoreUnusualPort})");
            }

            result.Score = Math.Min(100, score);
            result.IsDangerous = result.Score > _config.DangerousThresholdPercent;

            return result;
        }
    


        public (bool success, string message) SetKnownHash(string exePath, string sha256, string description)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return (false, "Đường dẫn không được để trống.");

            sha256 = (sha256 ?? string.Empty).Trim().ToLowerInvariant();

            if (!IsValidSha256(sha256))
                return (false, "Hash SHA-256 không hợp lệ - phải gồm đúng 64 ký tự hex (0-9, a-f).");

            var entries = _knownHashes.Values.Where(e => e.ExePath != exePath).ToList();
            entries.Add(new KnownHashEntry { ExePath = exePath, Sha256 = sha256, Description = description ?? string.Empty });
            SaveHashDb(entries);

            return (true, $"Đã lưu hash thủ công cho {exePath}.");
        }

        private static bool IsValidSha256(string hash)
        {
            if (hash.Length != 64) return false;
            foreach (var c in hash)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!isHex) return false;
            }
            return true;
        }

    }


}