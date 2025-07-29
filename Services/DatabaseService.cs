using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OutSystemsMcpServer.Data;
using OutSystemsMcpServer.Models;

namespace OutSystemsMcpServer.Services;

public interface IDatabaseService
{
    Task<List<DeploymentPlan>> GetAllDeploymentPlansAsync();
    Task<DeploymentPlan?> GetDeploymentPlanByKeyAsync(string planName, string deployedTo);
    Task<DeploymentPlan> SaveDeploymentPlanAsync(DeploymentPlan plan);
    Task<DeploymentPlan> UpdateDeploymentPlanAsync(DeploymentPlan plan);
    Task<List<DeploymentPlan>> GetChangedDeploymentPlansAsync();
    Task MarkNotificationAsSentAsync(int planId);
    Task InitializeDatabaseAsync();
    Task<List<DeploymentPlan>> GetDeploymentsByEnvironmentAsync(string environment);
    Task<List<DeploymentPlan>> GetRunningDeploymentsAsync();
    Task<List<DeploymentPlan>> GetRecentDeploymentsAsync(int hours);
    Task<List<DeploymentPlan>> SearchDeploymentsAsync(string searchTerm);
}

public class DatabaseService : IDatabaseService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(ApplicationDbContext context, ILogger<DatabaseService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task InitializeDatabaseAsync()
    {
        try
        {
            await _context.Database.EnsureCreatedAsync();
            _logger.LogInformation("Database initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
            throw;
        }
    }

    public async Task<List<DeploymentPlan>> GetAllDeploymentPlansAsync()
    {
        try
        {
            return await _context.DeploymentPlans
                .OrderByDescending(dp => dp.LastUpdated)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all deployment plans");
            throw;
        }
    }

    public async Task<DeploymentPlan?> GetDeploymentPlanByKeyAsync(string planName, string deployedTo)
    {
        try
        {
            return await _context.DeploymentPlans
                .FirstOrDefaultAsync(dp => dp.PlanName == planName && dp.DeployedTo == deployedTo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting deployment plan {PlanName} for {DeployedTo}", planName, deployedTo);
            throw;
        }
    }

    public async Task<DeploymentPlan> SaveDeploymentPlanAsync(DeploymentPlan plan)
    {
        try
        {
            var existingPlan = await GetDeploymentPlanByKeyAsync(plan.PlanName, plan.DeployedTo);
            
            if (existingPlan != null)
            {
                return await UpdateDeploymentPlanAsync(plan);
            }
            
            plan.FirstDetected = DateTime.Now;
            plan.LastUpdated = DateTime.Now;
            
            _context.DeploymentPlans.Add(plan);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("New deployment plan saved: {PlanName} -> {DeployedTo}", plan.PlanName, plan.DeployedTo);
            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving deployment plan");
            throw;
        }
    }

    public async Task<DeploymentPlan> UpdateDeploymentPlanAsync(DeploymentPlan plan)
    {
        try
        {
            var existingPlan = await GetDeploymentPlanByKeyAsync(plan.PlanName, plan.DeployedTo);
            
            if (existingPlan == null)
            {
                return await SaveDeploymentPlanAsync(plan);
            }

            // Detect status changes
            var hasStatusChanged = existingPlan.Status != plan.Status;
            
            if (hasStatusChanged)
            {
                existingPlan.PreviousStatus = existingPlan.Status;
                existingPlan.HasStatusChanged = true;
                existingPlan.NotificationSent = false;
                
                // Evaluate states before updating
                var wasRunning = existingPlan.IsRunning;
                var isNowFinished = plan.IsFinished;
                var isNowRunning = plan.IsRunning;
                
                // Calculate duration if changed from Running to Finished
                if (wasRunning && isNowFinished)
                {
                    existingPlan.EndTime = DateTime.Now;
                    if (existingPlan.StartTime.HasValue)
                    {
                        existingPlan.Duration = existingPlan.EndTime.Value - existingPlan.StartTime.Value;
                    }
                }
                
                // Set start time if changed to Running
                if (!wasRunning && isNowRunning)
                {
                    existingPlan.StartTime = DateTime.Now;
                }
                
                _logger.LogInformation("Status change detected: {PlanName} -> {DeployedTo}: {OldStatus} -> {NewStatus}", 
                    plan.PlanName, plan.DeployedTo, existingPlan.Status, plan.Status);
            }

            // Update properties
            existingPlan.Status = plan.Status;
            existingPlan.Details = plan.Details;
            existingPlan.ProcessedDetails = plan.ProcessedDetails;
            existingPlan.LastUpdated = DateTime.Now;
            existingPlan.Notes = plan.Notes;

            await _context.SaveChangesAsync();
            
            return existingPlan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating deployment plan");
            throw;
        }
    }

    public async Task<List<DeploymentPlan>> GetChangedDeploymentPlansAsync()
    {
        try
        {
            return await _context.DeploymentPlans
                .Where(dp => dp.HasStatusChanged && !dp.NotificationSent)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting plans with changes");
            throw;
        }
    }

    public async Task MarkNotificationAsSentAsync(int planId)
    {
        try
        {
            var plan = await _context.DeploymentPlans.FindAsync(planId);
            if (plan != null)
            {
                plan.NotificationSent = true;
                plan.HasStatusChanged = false;
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification as sent for plan {PlanId}", planId);
            throw;
        }
    }

    public async Task<List<DeploymentPlan>> GetDeploymentsByEnvironmentAsync(string environment)
    {
        try
        {
            var query = _context.DeploymentPlans.AsQueryable();
            
            if (!string.IsNullOrEmpty(environment))
            {
                query = query.Where(dp => dp.DeployedTo.Contains(environment));
            }
            
            return await query
                .OrderByDescending(dp => dp.LastUpdated)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting deployments by environment {Environment}", environment);
            throw;
        }
    }

    public async Task<List<DeploymentPlan>> GetRunningDeploymentsAsync()
    {
        try
        {
            return await _context.DeploymentPlans
                .Where(dp => dp.Status.Contains("Running"))
                .OrderByDescending(dp => dp.StartTime)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting running deployments");
            throw;
        }
    }

    public async Task<List<DeploymentPlan>> GetRecentDeploymentsAsync(int hours)
    {
        try
        {
            var since = DateTime.Now.AddHours(-hours);
            return await _context.DeploymentPlans
                .Where(dp => dp.LastUpdated >= since)
                .OrderByDescending(dp => dp.LastUpdated)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent deployments");
            throw;
        }
    }

    public async Task<List<DeploymentPlan>> SearchDeploymentsAsync(string searchTerm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetAllDeploymentPlansAsync();
            }

            var lowerSearch = searchTerm.ToLower();
            return await _context.DeploymentPlans
                .Where(dp => dp.PlanName.ToLower().Contains(lowerSearch) ||
                            dp.Details.ToLower().Contains(lowerSearch) ||
                            dp.ProcessedDetails.ToLower().Contains(lowerSearch) ||
                            dp.Status.ToLower().Contains(lowerSearch) ||
                            dp.DeployedTo.ToLower().Contains(lowerSearch))
                .OrderByDescending(dp => dp.LastUpdated)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching deployments with term {SearchTerm}", searchTerm);
            throw;
        }
    }
}