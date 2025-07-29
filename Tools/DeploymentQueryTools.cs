using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OutSystemsMcpServer.Models;
using OutSystemsMcpServer.Services;
using System.ComponentModel;

namespace OutSystemsMcpServer.Tools;

[McpServerToolType]
public class DeploymentQueryTools
{
    private readonly IDatabaseService _databaseService;
    private readonly IScrapingService _scrapingService;
    private readonly ILogger<DeploymentQueryTools> _logger;

    public DeploymentQueryTools(IDatabaseService databaseService, IScrapingService scrapingService, ILogger<DeploymentQueryTools> logger)
    {
        _databaseService = databaseService;
        _scrapingService = scrapingService;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Gets applications that are currently in deployment process")]
    public async Task<DeploymentQueryResult> GetDeploymentsInProgress(
        [Description("Optional environment: Homologation, Production")] string? environment = null)
    {
        try
        {
            _logger.LogInformation("Querying deployments in progress for environment: {Environment}", environment ?? "all");
            
            var runningDeployments = await _databaseService.GetRunningDeploymentsAsync();
            
            if (!string.IsNullOrEmpty(environment))
            {
                runningDeployments = runningDeployments
                    .Where(dp => dp.DeployedTo.Contains(environment, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var result = new DeploymentQueryResult
            {
                Success = true,
                Count = runningDeployments.Count,
                Message = runningDeployments.Count > 0 
                    ? $"Found {runningDeployments.Count} deployment(s) in progress"
                    : "No deployments in progress currently",
                Deployments = runningDeployments.Select(dp => new DeploymentInfo
                {
                    PlanName = dp.PlanName,
                    DeployedTo = dp.DeployedTo,
                    Status = dp.Status,
                    ProcessedDetails = dp.ProcessedDetails,
                    StartTime = dp.StartTime,
                    Duration = dp.StartTime.HasValue 
                        ? DateTime.Now - dp.StartTime.Value 
                        : null
                }).ToList()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying deployments in progress");
            return new DeploymentQueryResult
            {
                Success = false,
                Message = $"Error querying deployments: {ex.Message}"
            };
        }
    }

    [McpServerTool]
    [Description("Gets deployments by specific environment")]
    public async Task<DeploymentQueryResult> GetDeploymentsByEnvironment(
        [Description("Environment: Homologation, Production, etc.")] string environment)
    {
        try
        {
            _logger.LogInformation("Querying deployments for environment: {Environment}", environment);
            
            var deployments = await _databaseService.GetDeploymentsByEnvironmentAsync(environment);

            var result = new DeploymentQueryResult
            {
                Success = true,
                Count = deployments.Count,
                Message = $"Found {deployments.Count} deployment(s) in {environment}",
                Deployments = deployments.Select(dp => new DeploymentInfo
                {
                    PlanName = dp.PlanName,
                    DeployedTo = dp.DeployedTo,
                    Status = dp.Status,
                    ProcessedDetails = dp.ProcessedDetails,
                    StartTime = dp.StartTime,
                    EndTime = dp.EndTime,
                    Duration = dp.Duration,
                    LastUpdated = dp.LastUpdated
                }).ToList()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying deployments by environment");
            return new DeploymentQueryResult
            {
                Success = false,
                Message = $"Error querying deployments: {ex.Message}"
            };
        }
    }

    [McpServerTool]
    [Description("Gets deployment history for a specific application")]
    public async Task<DeploymentQueryResult> GetDeploymentHistory(
        [Description("Application name to search")] string applicationName,
        [Description("Number of days back (default 30)")] int days = 30)
    {
        try
        {
            _logger.LogInformation("Querying deployment history for: {ApplicationName}", applicationName);
            
            var deployments = await _databaseService.SearchDeploymentsAsync(applicationName);
            var cutoffDate = DateTime.Now.AddDays(-days);
            
            deployments = deployments
                .Where(dp => dp.LastUpdated >= cutoffDate)
                .OrderByDescending(dp => dp.LastUpdated)
                .ToList();

            var result = new DeploymentQueryResult
            {
                Success = true,
                Count = deployments.Count,
                Message = $"Deployment history for '{applicationName}' (last {days} days)",
                Deployments = deployments.Select(dp => new DeploymentInfo
                {
                    PlanName = dp.PlanName,
                    DeployedTo = dp.DeployedTo,
                    Status = dp.Status,
                    ProcessedDetails = dp.ProcessedDetails,
                    StartTime = dp.StartTime,
                    EndTime = dp.EndTime,
                    Duration = dp.Duration,
                    LastUpdated = dp.LastUpdated
                }).ToList()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying deployment history");
            return new DeploymentQueryResult
            {
                Success = false,
                Message = $"Error querying history: {ex.Message}"
            };
        }
    }

    [McpServerTool]
    [Description("Gets recent deployments in the last specified hours")]
    public async Task<DeploymentQueryResult> GetRecentDeployments(
        [Description("Number of hours back (default 24)")] int hours = 24)
    {
        try
        {
            _logger.LogInformation("Querying deployments from the last {Hours} hours", hours);
            
            var deployments = await _databaseService.GetRecentDeploymentsAsync(hours);

            var result = new DeploymentQueryResult
            {
                Success = true,
                Count = deployments.Count,
                Message = $"Deployments from the last {hours} hours",
                Deployments = deployments.Select(dp => new DeploymentInfo
                {
                    PlanName = dp.PlanName,
                    DeployedTo = dp.DeployedTo,
                    Status = dp.Status,
                    ProcessedDetails = dp.ProcessedDetails,
                    StartTime = dp.StartTime,
                    EndTime = dp.EndTime,
                    Duration = dp.Duration,
                    LastUpdated = dp.LastUpdated
                }).ToList()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying recent deployments");
            return new DeploymentQueryResult
            {
                Success = false,
                Message = $"Error querying recent deployments: {ex.Message}"
            };
        }
    }

    [McpServerTool]
    [Description("Updates deployment information from OutSystems")]
    public async Task<UpdateResult> RefreshDeploymentData()
    {
        try
        {
            _logger.LogInformation("Updating deployment information from OutSystems");
            
            var deploymentPlans = await _scrapingService.ScrapeDeploymentPlansAsync();
            
            if (deploymentPlans.Count == 0)
            {
                return new UpdateResult
                {
                    Success = false,
                    Message = "Could not get data from OutSystems. Check connection and credentials."
                };
            }

            int updated = 0;
            int added = 0;

            foreach (var plan in deploymentPlans)
            {
                var existing = await _databaseService.GetDeploymentPlanByKeyAsync(plan.PlanName, plan.DeployedTo);
                if (existing != null)
                {
                    await _databaseService.UpdateDeploymentPlanAsync(plan);
                    updated++;
                }
                else
                {
                    await _databaseService.SaveDeploymentPlanAsync(plan);
                    added++;
                }
            }

            return new UpdateResult
            {
                Success = true,
                Message = $"Update completed. New: {added}, Updated: {updated}",
                DeploymentsAdded = added,
                DeploymentsUpdated = updated,
                TotalDeployments = deploymentPlans.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating deployment data");
            return new UpdateResult
            {
                Success = false,
                Message = $"Error updating data: {ex.Message}"
            };
        }
    }
}

// Clases de resultado
public class DeploymentQueryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<DeploymentInfo> Deployments { get; set; } = new();
}

public class DeploymentInfo
{
    public string PlanName { get; set; } = string.Empty;
    public string DeployedTo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ProcessedDetails { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class UpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DeploymentsAdded { get; set; }
    public int DeploymentsUpdated { get; set; }
    public int TotalDeployments { get; set; }
}