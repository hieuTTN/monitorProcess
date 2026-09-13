namespace monitorProcess.Models
{
    public class WhitelistEntry
    {
        public string ProcessName { get; set; } = string.Empty;
        public string ExpectedSha256 { get; set; } = string.Empty;
    }
}