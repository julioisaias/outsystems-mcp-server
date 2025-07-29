using System.ComponentModel.DataAnnotations;

namespace OutSystemsMcpServer.Models;

public class DeploymentPlan
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string PlanName { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string DeployedTo { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string Status { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string Details { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string ProcessedDetails { get; set; } = string.Empty;
    
    public DateTime? StartTime { get; set; }
    
    public DateTime? EndTime { get; set; }
    
    public TimeSpan? Duration { get; set; }
    
    public DateTime FirstDetected { get; set; }
    
    public DateTime LastUpdated { get; set; }
    
    [MaxLength(50)]
    public string PreviousStatus { get; set; } = string.Empty;
    
    public bool HasStatusChanged { get; set; }
    
    public bool NotificationSent { get; set; }
    
    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Calculated properties
    public bool IsRunning => Status.Contains("Running", StringComparison.OrdinalIgnoreCase);
    
    public bool IsFinished => Status.Contains("Finished", StringComparison.OrdinalIgnoreCase) || Status.Contains("Successfully", StringComparison.OrdinalIgnoreCase);
    
    public bool IsHomologation => DeployedTo.Contains("Homologation", StringComparison.OrdinalIgnoreCase);
    
    public bool IsProduction => DeployedTo.Contains("Production", StringComparison.OrdinalIgnoreCase);
    
    public string GetNotificationMessage()
    {
        var environment = IsHomologation ? "Homologation" : 
                         IsProduction ? "Production" : 
                         DeployedTo;
        
        string action;
        string article;
        bool isMultiple = ProcessedDetails == "Multiple applications";
        
        if (IsRunning)
        {
            action = isMultiple ? "are deploying" : "is deploying";
            article = isMultiple ? "their deployments" : "its deployment";
        }
        else if (IsFinished)
        {
            action = isMultiple ? "have finished" : "has finished";
            article = isMultiple ? "their deployments" : "its deployment";
        }
        else
        {
            action = isMultiple ? "are in process" : "is in process";
            article = isMultiple ? "their deployments" : "its deployment";
        }
        
        var duration = Duration.HasValue ? $" (Duration: {Duration.Value:hh\\:mm\\:ss})" : "";
        
        return $"{ProcessedDetails} {action} {article} to {environment}{duration}";
    }
}