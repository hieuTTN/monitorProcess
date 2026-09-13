using System.Collections.Generic;

namespace monitorProcess.Models
{
    /// <summary>
    /// Cấu hình hệ thống chấm điểm rủi ro (Risk Scoring) - mỗi tiêu chí vi phạm
    /// cộng thêm 1 số điểm, tổng điểm vượt ngưỡng % thì coi là nguy hiểm.
    /// </summary>
    public class RiskScoringConfig
    {
        public bool Enabled { get; set; } = true;

        public int ScoreRunningFromTmp { get; set; } = 30;

        public bool EnableHashDatabaseCheck { get; set; } = true;
        public int ScoreHashNotInDatabase { get; set; } = 20;
        public int ScoreExecutableChanged { get; set; } = 30;

        public bool EnableCpuCheck { get; set; } = true;
        public int ScoreHighCpu { get; set; } = 15;
        public double CpuThresholdForScoring { get; set; } = 90;

        public bool EnablePortCheck { get; set; } = true;
        public int ScoreUnusualPort { get; set; } = 20;

        /// <summary>Danh sách cổng LISTEN được coi là "quen thuộc" - cổng ngoài danh sách này bị tính là "lạ".</summary>
        public List<int> TrustedPorts { get; set; } = new() { 22, 25, 53, 80, 443, 3306, 5432, 8080 };

        /// <summary>Ngưỡng % để coi tiến trình là nguy hiểm (điểm tối đa chuẩn hóa về 100).</summary>
        public double DangerousThresholdPercent { get; set; } = 50;
    }
}