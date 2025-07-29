using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OutSystemsMcpServer.Models;
using OutSystemsMcpServer.Services;
using System.ComponentModel;

namespace OutSystemsMcpServer.Tools;

[McpServerToolType]
public class ApplicationStatusTools
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<ApplicationStatusTools> _logger;

    public ApplicationStatusTools(IDatabaseService databaseService, ILogger<ApplicationStatusTools> logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Gets the current status of a specific application")]
    public async Task<ApplicationStatusResult> GetApplicationStatus(
        [Description("Application name")] string applicationName)
    {
        try
        {
            _logger.LogInformation("Querying application status: {ApplicationName}", applicationName);
            
            var deployments = await _databaseService.SearchDeploymentsAsync(applicationName);
            
            if (deployments.Count == 0)
            {
                return new ApplicationStatusResult
                {
                    Success = true,
                    ApplicationName = applicationName,
                    Message = $"No deployments found for application '{applicationName}'"
                };
            }

            // Get the most recent deployment
            var latestDeployment = deployments
                .OrderByDescending(dp => dp.LastUpdated)
                .First();

            // Find active deployments
            var activeDeployments = deployments
                .Where(dp => dp.IsRunning)
                .ToList();

            // Recent history (last 7 days)
            var recentHistory = deployments
                .Where(dp => dp.LastUpdated >= DateTime.Now.AddDays(-7))
                .OrderByDescending(dp => dp.LastUpdated)
                .Take(5)
                .ToList();

            return new ApplicationStatusResult
            {
                Success = true,
                ApplicationName = applicationName,
                CurrentStatus = latestDeployment.Status,
                Environment = latestDeployment.DeployedTo,
                LastUpdated = latestDeployment.LastUpdated,
                IsRunning = latestDeployment.IsRunning,
                ActiveDeployments = activeDeployments.Count,
                Message = GenerateStatusMessage(latestDeployment, activeDeployments.Count),
                RecentHistory = recentHistory.Select(dp => new DeploymentSummary
                {
                    Environment = dp.DeployedTo,
                    Status = dp.Status,
                    Date = dp.LastUpdated,
                    Duration = dp.Duration
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying application status");
            return new ApplicationStatusResult
            {
                Success = false,
                ApplicationName = applicationName,
                Message = $"Error querying status: {ex.Message}"
            };
        }
    }

    [McpServerTool]
    [Description("Search deployments by name, status or details")]
    public async Task<SearchResult> SearchDeployments(
        [Description("Search term")] string searchTerm,
        [Description("Maximum number of results (default 20)")] int maxResults = 20)
    {
        try
        {
            _logger.LogInformation("Searching deployments with term: {SearchTerm}", searchTerm);
            
            var deployments = await _databaseService.SearchDeploymentsAsync(searchTerm);
            var totalFound = deployments.Count;
            
            // Limit results
            deployments = deployments.Take(maxResults).ToList();

            return new SearchResult
            {
                Success = true,
                SearchTerm = searchTerm,
                TotalFound = totalFound,
                ResultsReturned = deployments.Count,
                Message = totalFound > maxResults 
                    ? $"Found {totalFound} results, showing first {maxResults}"
                    : $"Found {totalFound} results",
                Results = deployments.Select(dp => new SearchResultItem
                {
                    PlanName = dp.PlanName,
                    Environment = dp.DeployedTo,
                    Status = dp.Status,
                    ApplicationDetails = dp.ProcessedDetails,
                    LastUpdated = dp.LastUpdated,
                    IsActive = dp.IsRunning
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching deployments");
            return new SearchResult
            {
                Success = false,
                SearchTerm = searchTerm,
                Message = $"Error searching: {ex.Message}"
            };
        }
    }

    [McpServerTool]
    [Description("Gets general deployment statistics")]
    public async Task<DeploymentStatistics> GetDeploymentStatistics(
        [Description("Number of days for statistics (default 30)")] int days = 30)
    {
        try
        {
            _logger.LogInformation("Generating deployment statistics for {Days} days", days);
            
            var cutoffDate = DateTime.Now.AddDays(-days);
            var allDeployments = await _databaseService.GetAllDeploymentPlansAsync();
            var recentDeployments = allDeployments
                .Where(dp => dp.LastUpdated >= cutoffDate)
                .ToList();

            // Statistics by environment
            var environmentStats = recentDeployments
                .GroupBy(dp => dp.DeployedTo)
                .Select(g => new EnvironmentStatistic
                {
                    Environment = g.Key,
                    TotalDeployments = g.Count(),
                    SuccessfulDeployments = g.Count(dp => dp.IsFinished),
                    ActiveDeployments = g.Count(dp => dp.IsRunning),
                    AverageDuration = g.Where(dp => dp.Duration.HasValue)
                        .Select(dp => dp.Duration!.Value.TotalMinutes)
                        .DefaultIfEmpty(0)
                        .Average()
                })
                .OrderByDescending(es => es.TotalDeployments)
                .ToList();

            // Top applications
            var topApplications = recentDeployments
                .Where(dp => !string.IsNullOrEmpty(dp.ProcessedDetails))
                .GroupBy(dp => dp.ProcessedDetails)
                .Select(g => new ApplicationStatistic
                {
                    ApplicationName = g.Key,
                    DeploymentCount = g.Count(),
                    LastDeployment = g.Max(dp => dp.LastUpdated)
                })
                .OrderByDescending(a => a.DeploymentCount)
                .Take(10)
                .ToList();

            // Activity by day of week
            var dailyActivity = recentDeployments
                .GroupBy(dp => dp.LastUpdated.DayOfWeek)
                .Select(g => new DailyActivity
                {
                    DayOfWeek = g.Key.ToString(),
                    DeploymentCount = g.Count()
                })
                .OrderBy(da => (int)Enum.Parse<DayOfWeek>(da.DayOfWeek))
                .ToList();

            return new DeploymentStatistics
            {
                Success = true,
                PeriodDays = days,
                TotalDeployments = recentDeployments.Count,
                ActiveDeployments = recentDeployments.Count(dp => dp.IsRunning),
                SuccessfulDeployments = recentDeployments.Count(dp => dp.IsFinished),
                UniqueApplications = recentDeployments
                    .Select(dp => dp.ProcessedDetails)
                    .Distinct()
                    .Count(),
                EnvironmentStatistics = environmentStats,
                TopApplications = topApplications,
                DailyActivity = dailyActivity,
                Message = $"Statistics for the last {days} days"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating statistics");
            return new DeploymentStatistics
            {
                Success = false,
                PeriodDays = days,
                Message = $"Error generating statistics: {ex.Message}"
            };
        }
    }

    [McpServerTool]
    [Description("Checks which applications have pending deployments or haven't been deployed recently")]
    public async Task<PendingDeploymentsResult> GetPendingDeployments(
        [Description("Environment to check (Homologation, Production)")] string environment,
        [Description("Days without deployment to consider pending (default 7)")] int daysSinceLastDeployment = 7)
    {
        try
        {
            _logger.LogInformation("Checking pending deployments for {Environment}", environment);
            
            var cutoffDate = DateTime.Now.AddDays(-daysSinceLastDeployment);
            var allDeployments = await _databaseService.GetDeploymentsByEnvironmentAsync(environment);
            
            // Group by application and find the last deployment
            var applicationGroups = allDeployments
                .Where(dp => !string.IsNullOrEmpty(dp.ProcessedDetails))
                .GroupBy(dp => dp.ProcessedDetails)
                .Select(g => new
                {
                    Application = g.Key,
                    LastDeployment = g.OrderByDescending(dp => dp.LastUpdated).FirstOrDefault(),
                    DeploymentCount = g.Count()
                })
                .ToList();

            // Applications without recent deployments
            var pendingApplications = applicationGroups
                .Where(ag => ag.LastDeployment?.LastUpdated < cutoffDate)
                .Select(ag => new PendingApplication
                {
                    ApplicationName = ag.Application,
                    LastDeploymentDate = ag.LastDeployment?.LastUpdated,
                    LastStatus = ag.LastDeployment?.Status ?? "N/A",
                    DaysSinceLastDeployment = ag.LastDeployment != null 
                        ? (int)(DateTime.Now - ag.LastDeployment.LastUpdated).TotalDays
                        : -1
                })
                .OrderByDescending(pa => pa.DaysSinceLastDeployment)
                .ToList();

            // Applications with failed deployments
            var failedDeployments = allDeployments
                .Where(dp => !dp.IsFinished && !dp.IsRunning && dp.LastUpdated >= cutoffDate)
                .Select(dp => new PendingApplication
                {
                    ApplicationName = dp.ProcessedDetails,
                    LastDeploymentDate = dp.LastUpdated,
                    LastStatus = dp.Status,
                    DaysSinceLastDeployment = (int)(DateTime.Now - dp.LastUpdated).TotalDays,
                    IsFailed = true
                })
                .ToList();

            return new PendingDeploymentsResult
            {
                Success = true,
                Environment = environment,
                PendingApplications = pendingApplications,
                FailedDeployments = failedDeployments,
                TotalPending = pendingApplications.Count,
                TotalFailed = failedDeployments.Count,
                Message = $"Found {pendingApplications.Count} applications without recent deployment and {failedDeployments.Count} with failed deployments"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking pending deployments");
            return new PendingDeploymentsResult
            {
                Success = false,
                Environment = environment,
                Message = $"Error checking pending deployments: {ex.Message}"
            };
        }
    }

    private string GenerateStatusMessage(DeploymentPlan deployment, int activeCount)
    {
        if (deployment.IsRunning)
        {
            var duration = deployment.StartTime.HasValue 
                ? $" (in progress for {(DateTime.Now - deployment.StartTime.Value):hh\\:mm\\:ss})"
                : "";
            return $"The application is being deployed to {deployment.DeployedTo}{duration}";
        }
        else if (deployment.IsFinished)
        {
            var duration = deployment.Duration.HasValue 
                ? $" (duration: {deployment.Duration.Value:hh\\:mm\\:ss})"
                : "";
            return $"Last successful deployment to {deployment.DeployedTo}{duration}";
        }
        else
        {
            return $"Current status: {deployment.Status} in {deployment.DeployedTo}";
        }
    }
}

// Clases de resultado
public class ApplicationStatusResult
{
    public bool Success { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public bool IsRunning { get; set; }
    public int ActiveDeployments { get; set; }
    public List<DeploymentSummary> RecentHistory { get; set; } = new();
}

public class DeploymentSummary
{
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public TimeSpan? Duration { get; set; }
}

public class SearchResult
{
    public bool Success { get; set; }
    public string SearchTerm { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int TotalFound { get; set; }
    public int ResultsReturned { get; set; }
    public List<SearchResultItem> Results { get; set; } = new();
}

public class SearchResultItem
{
    public string PlanName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ApplicationDetails { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public bool IsActive { get; set; }
}

public class DeploymentStatistics
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PeriodDays { get; set; }
    public int TotalDeployments { get; set; }
    public int ActiveDeployments { get; set; }
    public int SuccessfulDeployments { get; set; }
    public int UniqueApplications { get; set; }
    public List<EnvironmentStatistic> EnvironmentStatistics { get; set; } = new();
    public List<ApplicationStatistic> TopApplications { get; set; } = new();
    public List<DailyActivity> DailyActivity { get; set; } = new();
}

public class EnvironmentStatistic
{
    public string Environment { get; set; } = string.Empty;
    public int TotalDeployments { get; set; }
    public int SuccessfulDeployments { get; set; }
    public int ActiveDeployments { get; set; }
    public double AverageDuration { get; set; }
}

public class ApplicationStatistic
{
    public string ApplicationName { get; set; } = string.Empty;
    public int DeploymentCount { get; set; }
    public DateTime LastDeployment { get; set; }
}

public class DailyActivity
{
    public string DayOfWeek { get; set; } = string.Empty;
    public int DeploymentCount { get; set; }
}

public class PendingDeploymentsResult
{
    public bool Success { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int TotalPending { get; set; }
    public int TotalFailed { get; set; }
    public List<PendingApplication> PendingApplications { get; set; } = new();
    public List<PendingApplication> FailedDeployments { get; set; } = new();
}

public class PendingApplication
{
    public string ApplicationName { get; set; } = string.Empty;
    public DateTime? LastDeploymentDate { get; set; }
    public string LastStatus { get; set; } = string.Empty;
    public int DaysSinceLastDeployment { get; set; }
    public bool IsFailed { get; set; }
}