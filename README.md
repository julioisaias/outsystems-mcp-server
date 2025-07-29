# OutSystems MCP Server

An MCP (Model Context Protocol) server that enables AI assistants to query deployment information from OutSystems LifeTime.

## Important: Web Scraping Approach

**This server uses web scraping, NOT the OutSystems API.** This design choice makes it accessible to developers who:
- Don't have API access enabled in their OutSystems environment
- Need to query LifeTime without administrator permissions
- Want to monitor deployments without requiring API configuration

The server authenticates using standard LifeTime credentials and extracts data directly from the web interface using Playwright.

## Overview

OutSystemsMcpServer provides a bridge between AI assistants and OutSystems LifeTime, allowing you to:
- Query deployment status and history
- Monitor applications in deployment
- Get deployment statistics
- Track pending deployments

The server uses web scraping to extract data from the OutSystems LifeTime interface and caches it in a local SQLite database.

## Features

- **Real-time Deployment Monitoring**: Track deployments as they progress through environments
- **Historical Data**: Query past deployments and their outcomes
- **Caching**: SQLite database reduces load on OutSystems servers
- **MCP Protocol**: Compatible with any MCP-enabled AI assistant
- **Structured Logging**: Comprehensive logs for debugging and monitoring

## Prerequisites

- .NET 9.0 SDK
- OutSystems LifeTime access credentials (regular user account - no API access required)
- Chrome browser (for Playwright web scraping)

## Installation

1. Clone the repository:
```bash
git clone https://github.com/julioisaias/outsystems-mcp-server.git
cd outsystems-mcp-server
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Install Playwright browsers:
```bash
dotnet run -- install chromium
```

4. Configure your settings:
```bash
cp appsettings.Example.json appsettings.json
```

5. Edit `appsettings.json` with your OutSystems credentials:
```json
{
  "OutSystemsSettings": {
    "LoginUrl": "https://your-outsystems-url/lifetime",
    "StagingListUrl": "https://your-outsystems-url/lifetime/Stagings_List.aspx",
    "Username": "your-username",
    "Password": "your-password"
  }
}
```

## Usage

### Running the Server

Start the MCP server:
```bash
dotnet run
```

Or use the provided batch file:
```bash
start-server.bat
```

### Connecting to AI Assistants

#### Claude Desktop

Add to your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "outsystems": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/outsystems-mcp-server"],
      "env": {}
    }
  }
}
```

#### Other MCP Clients

The server communicates via stdio and is compatible with any MCP client implementation.

## Available Tools

### get_deployment_status
Query current deployment status across environments.

Parameters:
- `applicationName` (optional): Filter by application name
- `environment` (optional): Filter by target environment

### get_deployment_history
Retrieve historical deployment data.

Parameters:
- `applicationName` (optional): Filter by application name
- `days` (optional): Number of days to look back (default: 7)

### get_pending_deployments
List all deployments currently in progress or waiting.

### get_deployment_statistics
Get aggregated statistics about deployments.

Parameters:
- `days` (optional): Number of days to analyze (default: 30)

## Architecture

```
OutSystemsMcpServer/
├── Data/                    # Entity Framework context
├── Models/                  # Data models
├── Services/               # Core services
│   ├── DatabaseService.cs  # SQLite operations
│   └── ScrapingService.cs  # Web scraping logic
├── Tools/                  # MCP tool implementations
└── Program.cs             # Application entry point
```

### Key Components

- **MCP Server**: Uses ModelContextProtocol package for tool exposure
- **Web Scraping**: Microsoft.Playwright for reliable data extraction
- **Data Storage**: SQLite via Entity Framework Core
- **Logging**: Serilog for structured logging

## Development

### Building

```bash
dotnet build
```

### Running in Debug Mode

```bash
dotnet run --configuration Debug
```

### Adding New Tools

1. Create a new class in the `Tools/` directory
2. Decorate the class with `[McpServerToolType]`
3. Add methods decorated with `[McpServerTool]` and `[Description]`
4. Register the class in `Program.cs`

Example:
```csharp
[McpServerToolType]
public class MyNewTools
{
    [McpServerTool]
    [Description("Description of what this tool does")]
    public async Task<object> my_new_tool(string parameter)
    {
        // Implementation
    }
}
```

## Configuration

Configuration is managed through `appsettings.json`:

- `OutSystemsSettings`: OutSystems connection parameters
- `Logging`: Log levels and output configuration

## Troubleshooting

### Common Issues

1. **Authentication Failures**: Verify your OutSystems credentials and URL
2. **Scraping Errors**: Ensure Chrome is installed via `dotnet run -- install chromium`
3. **Database Errors**: Delete `outsystems_mcp.db` to reset the cache

### Logs

Check the `logs/` directory for detailed error information:
- `mcp-server-YYYYMMDD.log`: Daily log files with all events

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Security

- Never commit `appsettings.json` with real credentials
- Use environment variables for sensitive data in production
- The `.gitignore` file excludes sensitive files by default

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built using the [Model Context Protocol](https://modelcontextprotocol.io/)
- Powered by [Microsoft.Playwright](https://playwright.dev/dotnet/)
- Database operations via [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/)