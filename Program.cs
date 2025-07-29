using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using OutSystemsMcpServer;
using OutSystemsMcpServer.Data;
using OutSystemsMcpServer.Models;
using OutSystemsMcpServer.Services;
using OutSystemsMcpServer.Tools;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Logging;
using System.Reflection;

// Configure Serilog for MCP Server
// IMPORTANT: Only write to file, NEVER to Console/stdout to avoid interfering with MCP protocol
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File("logs/mcp-server-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Capture global exceptions to prevent them from interfering with MCP
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    Log.Fatal(e.ExceptionObject as Exception, "Unhandled global exception");
};

TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    Log.Fatal(e.Exception, "Unobserved task exception");
    e.SetObserved();
};

try
{
    Log.Information("Starting OutSystems MCP Server");

    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog as the logger
    builder.Services.AddSerilog();

    // Load configuration from the executable directory
    var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
    var projectDirectory = Path.GetDirectoryName(assemblyLocation) ?? Directory.GetCurrentDirectory();
    
    // In Release mode, go up 3 levels from bin/Release/net9.0
    if (projectDirectory.Contains("bin"))
    {
        projectDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "..", "..", ".."));
    }
    
    builder.Configuration
        .SetBasePath(projectDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddEnvironmentVariables();

    // Configure AppConfiguration
    var appConfig = builder.Configuration.GetSection("OutSystemsSettings").Get<AppConfiguration>() 
        ?? throw new InvalidOperationException("OutSystems configuration not found");
    builder.Services.AddSingleton(appConfig);

    // Configure Entity Framework with the correct path
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(projectDirectory, "outsystems_mcp.db")}"));

    // Register services
    builder.Services.AddScoped<IDatabaseService, DatabaseService>();
    builder.Services.AddSingleton<IScrapingService, ScrapingService>();

    // Register MCP tools
    builder.Services.AddScoped<DeploymentQueryTools>();
    builder.Services.AddScoped<ApplicationStatusTools>();

    // Configure MCP Server - use automatic tool scanning
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();  // Automatically scans all tools with [McpServerToolType]
    
    Console.Error.WriteLine("MCP Server configured with WithToolsFromAssembly()");

    var host = builder.Build();
    
    // Initialize the database synchronously before starting the host
    using (var scope = host.Services.CreateScope())
    {
        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        dbService.InitializeDatabaseAsync().GetAwaiter().GetResult();
        Log.Information("Database initialized");
    }

    // Informative logs are written only to file to avoid interfering with MCP
    Log.Information("OutSystems MCP Server configured successfully");
    Log.Information("Available tools:");
    Log.Information("- GetDeploymentsInProgress: Query deployments in progress");
    Log.Information("- GetDeploymentsByEnvironment: Query by environment");
    Log.Information("- GetDeploymentHistory: Application history");
    Log.Information("- GetRecentDeployments: Recent deployments");
    Log.Information("- RefreshDeploymentData: Refresh data from OutSystems");
    Log.Information("- GetApplicationStatus: Application status");
    Log.Information("- SearchDeployments: Search deployments");
    Log.Information("- GetDeploymentStatistics: General statistics");
    Log.Information("- GetPendingDeployments: Applications pending deployment");
    Log.Information("- TestOutSystemsConnection: Test OutSystems connection");
    Log.Information("- GetSessionStatus: Current session status");

    // Write to stderr for MCP diagnostics
    Console.Error.WriteLine($"OutSystems MCP Server started successfully");
    Console.Error.WriteLine($"Configuration directory: {projectDirectory}");
    Console.Error.WriteLine($"Configuration file: {Path.Combine(projectDirectory, "appsettings.json")}");
    
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "The server terminated unexpectedly");
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
}
finally
{
    Console.Error.WriteLine("MCP Server shutting down");
    Log.CloseAndFlush();
}
