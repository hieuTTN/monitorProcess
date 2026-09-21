using Avalonia.Media;

namespace monitorProcess.controllers
{
    public class ProcessRowViewModel
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
        public string CmdLine { get; set; } = string.Empty;
        public string Ports { get; set; } = string.Empty;
        public string AlertText { get; set; } = string.Empty;
        public bool IsSuspicious { get; set; }

        public double CpuPercent { get; set; }
        public double MemoryMb { get; set; }
        public double ReadSpeedBps { get; set; }
        public double WriteSpeedBps { get; set; }

        public double MemoryPercent { get; set; }

        public int RiskScore { get; set; }

        public string CategoryText { get; set; } = string.Empty;
        
        public string ResourceText => $"CPU: {CpuPercent:0.0}%   RAM: {MemoryMb:0.0} MB";

        public string IoText => $"Đọc: {FormatSpeed(ReadSpeedBps)}   Ghi: {FormatSpeed(WriteSpeedBps)}";

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024 * 1024)
                return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
            if (bytesPerSecond >= 1024)
                return $"{bytesPerSecond / 1024:0.0} KB/s";
            return $"{bytesPerSecond:0} B/s";
        }

        public IBrush RowBackground =>
            IsSuspicious
                ? new SolidColorBrush(Color.FromArgb(60, 220, 40, 40))
                : Brushes.Transparent;
    }
}