namespace OutSystemsMcpServer.Models;

public class AppConfiguration
{
    public string LoginUrl { get; set; } = string.Empty;
    public string StagingListUrl { get; set; } = string.Empty;
    public int MonitoringIntervalSeconds { get; set; } = 10;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableNotifications { get; set; } = true;
    public int SessionTimeoutMinutes { get; set; } = 30;
    public bool LazyInitialization { get; set; } = true;
}