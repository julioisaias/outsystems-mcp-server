# Script to install Playwright and its browsers
Write-Host "Installing Playwright..." -ForegroundColor Green

# Create a temporary project to run Playwright
$tempDir = "$env:TEMP\playwright-install"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# Create a temporary project
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Playwright" Version="1.54.0" />
  </ItemGroup>
</Project>
"@ | Out-File -FilePath "$tempDir\PlaywrightInstall.csproj"

# Create the main program
@"
using Microsoft.Playwright;

var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
return exitCode;
"@ | Out-File -FilePath "$tempDir\Program.cs"

# Change to temporary directory
Push-Location $tempDir

try {
    # Restore packages
    Write-Host "Restoring packages..." -ForegroundColor Yellow
    dotnet restore

    # Run the installation
    Write-Host "Installing Chromium..." -ForegroundColor Yellow
    dotnet run

    Write-Host "Installation completed!" -ForegroundColor Green
}
finally {
    # Return to original directory
    Pop-Location

    # Clean temporary files
    Remove-Item -Path $tempDir -Recurse -Force
}