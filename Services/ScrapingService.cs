using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using OutSystemsMcpServer.Models;
using System.Text.RegularExpressions;

namespace OutSystemsMcpServer.Services;

public interface IScrapingService
{
    Task<List<DeploymentPlan>> ScrapeDeploymentPlansAsync();
    Task<bool> LoginAsync();
    Task<bool> IsLoggedInAsync();
    void Dispose();
}

public class ScrapingService : IScrapingService, IDisposable
{
    private readonly AppConfiguration _config;
    private readonly ILogger<ScrapingService> _logger;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private DateTime _lastLoginTime;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed = false;

    public ScrapingService(AppConfiguration config, ILogger<ScrapingService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<List<DeploymentPlan>> ScrapeDeploymentPlansAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                _logger.LogError("Could not authenticate with OutSystems");
                return new List<DeploymentPlan>();
            }

            _logger.LogInformation("Starting deployment plans scraping");
            
            // Navegar a la página de staging list
            await _page!.GotoAsync(_config.StagingListUrl);
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Wait for the table to appear
            await _page.WaitForSelectorAsync("table.table", new() { Timeout = 30000 });

            // Extract data from the table
            var deploymentPlans = await ExtractDeploymentPlansFromTableAsync();
            
            _logger.LogInformation("Scraping completed. Found {Count} deployment plans", deploymentPlans.Count);
            
            return deploymentPlans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scraping");
            return new List<DeploymentPlan>();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> LoginAsync()
    {
        try
        {
            await InitializeBrowserAsync();
            
            _logger.LogInformation($"Starting login process at: {_config.LoginUrl}");
            
            // Navigate to login page
            var response = await _page!.GotoAsync(_config.LoginUrl);
            if (response != null)
            {
                _logger.LogInformation($"HTTP response: {response.Status} - {response.StatusText}");
            }
            
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            _logger.LogInformation($"Page loaded. Current URL: {_page.Url}");

            // Check if already logged in
            if (await IsLoggedInAsync())
            {
                _logger.LogInformation("Already logged in");
                return true;
            }

            // Find login fields
            var usernameSelector = "input[type='text'], input[name*='user'], input[id*='user']";
            var passwordSelector = "input[type='password']";
            var loginButtonSelector = "input[type='submit'], button[type='submit'], .btn-primary";

            try
            {
                _logger.LogInformation("Waiting for login fields...");
                await _page.WaitForSelectorAsync(usernameSelector, new() { Timeout = 10000 });
                await _page.WaitForSelectorAsync(passwordSelector, new() { Timeout = 10000 });
                _logger.LogInformation("Login fields found");
            }
            catch (TimeoutException)
            {
                _logger.LogError($"Login fields not found. URL: {_page.Url}");
                // Take screenshot for debug
                var screenshotPath = Path.Combine("logs", $"login-timeout-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                await _page.ScreenshotAsync(new() { Path = screenshotPath });
                _logger.LogError($"Screenshot saved at: {screenshotPath}");
                return false;
            }

            // Fill credentials
            _logger.LogInformation($"Filling credentials for user: {_config.Username}");
            await _page.FillAsync(usernameSelector, _config.Username);
            await _page.FillAsync(passwordSelector, _config.Password);

            // Click login button
            _logger.LogInformation("Clicking login button...");
            await _page.ClickAsync(loginButtonSelector);
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            
            _logger.LogInformation($"Login completed. Current URL: {_page.Url}");

            // Verify if login was successful
            var loginSuccess = await IsLoggedInAsync();
            if (loginSuccess)
            {
                _lastLoginTime = DateTime.Now;
                _logger.LogInformation("Login successful");
            }
            else
            {
                _logger.LogWarning("Login failed - taking screenshot for diagnostics");
                var screenshotPath = Path.Combine("logs", $"login-failed-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                await _page.ScreenshotAsync(new() { Path = screenshotPath });
                _logger.LogWarning($"Screenshot saved at: {screenshotPath}");
            }

            return loginSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            if (_page != null)
            {
                try
                {
                    var screenshotPath = Path.Combine("logs", $"login-error-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                    await _page.ScreenshotAsync(new() { Path = screenshotPath });
                    _logger.LogError($"Error screenshot saved at: {screenshotPath}");
                }
                catch { }
            }
            return false;
        }
    }

    public async Task<bool> IsLoggedInAsync()
    {
        try
        {
            if (_page == null) return false;

            // Check if we are on a page that requires authentication
            var url = _page.Url;
            if (url.Contains("login", StringComparison.OrdinalIgnoreCase) || 
                url.Contains("signin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Check if we can access the staging page
            var response = await _page.GotoAsync(_config.StagingListUrl);
            if (response?.Status == 200 && !_page.Url.Contains("login", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync()
    {
        // Check if we need to re-authenticate
        if (_page == null || 
            DateTime.Now - _lastLoginTime > TimeSpan.FromMinutes(_config.SessionTimeoutMinutes) ||
            !await IsLoggedInAsync())
        {
            return await LoginAsync();
        }

        return true;
    }

    private async Task InitializeBrowserAsync()
    {
        if (_browser == null)
        {
            try
            {
                _logger.LogInformation("Creating Playwright instance...");
                var playwright = await Playwright.CreateAsync();
                
                _logger.LogInformation("Launching Chromium browser...");
                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Args = new[] { 
                        "--no-sandbox", 
                        "--disable-setuid-sandbox",
                        "--ignore-certificate-errors",
                        "--ignore-certificate-errors-spki-list"
                    },
                    Timeout = 30000 // 30 seconds timeout
                });
                
                _logger.LogInformation("Creating browser context...");
                _context = await _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
                    IgnoreHTTPSErrors = true // Ignore SSL certificate errors
                });
                
                _page = await _context.NewPageAsync();
                
                // Add listener for browser console messages
                _page.Console += (_, msg) => _logger.LogDebug($"Browser console: {msg.Text}");
                
                _logger.LogInformation("Browser initialized successfully with self-signed certificate support");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing browser");
                throw;
            }
        }
    }

    private async Task<List<DeploymentPlan>> ExtractDeploymentPlansFromTableAsync()
    {
        var deploymentPlans = new List<DeploymentPlan>();

        try
        {
            // Find all table rows (excluding header)
            var rows = await _page!.QuerySelectorAllAsync("table.table tr:not(.table-header)");
            
            foreach (var row in rows)
            {
                try
                {
                    var cells = await row.QuerySelectorAllAsync("td");
                    if (cells.Count >= 4)
                    {
                        var planName = await cells[0].TextContentAsync() ?? "";
                        var deployedTo = await cells[1].TextContentAsync() ?? "";
                        var status = await cells[2].TextContentAsync() ?? "";
                        var details = await cells[3].TextContentAsync() ?? "";

                        // Clean and process data
                        planName = planName.Trim();
                        deployedTo = deployedTo.Trim();
                        status = CleanStatus(status.Trim());
                        details = details.Trim();

                        // Process details according to specified rules
                        var processedDetails = ProcessDetails(details);

                        if (!string.IsNullOrEmpty(planName) && !string.IsNullOrEmpty(deployedTo))
                        {
                            var deployment = new DeploymentPlan
                            {
                                PlanName = planName,
                                DeployedTo = deployedTo,
                                Status = status,
                                Details = details,
                                ProcessedDetails = processedDetails,
                                LastUpdated = DateTime.Now
                            };

                            deploymentPlans.Add(deployment);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing table row");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting data from table");
        }

        return deploymentPlans;
    }

    private string ProcessDetails(string details)
    {
        if (string.IsNullOrEmpty(details))
            return details;

        // Split by spaces or commas
        var parts = details.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 0)
            return details;

        // If it's a single application, return the full name
        if (parts.Length == 1)
        {
            return parts[0];
        }

        // If there are multiple applications, indicate "Multiple applications"
        return "Multiple applications";
    }

    private string CleanStatus(string status)
    {
        if (string.IsNullOrEmpty(status))
            return status;

        // Extract only the status, ignoring dates and times
        var statusParts = status.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (statusParts.Length > 0)
        {
            return statusParts[0].Trim();
        }

        return status;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _page?.CloseAsync().Wait();
            _context?.CloseAsync().Wait();
            _browser?.CloseAsync().Wait();
            _semaphore.Dispose();
            _disposed = true;
        }
    }
}