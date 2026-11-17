#!/usr/bin/env pwsh
# User Secrets Setup Script for Dataverse.Fluent Integration Tests
# This script helps you quickly configure User Secrets for local development

Write-Host "?" -ForegroundColor Green -NoNewline
Write-Host " Dataverse.Fluent - User Secrets Setup" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the correct directory
$projectFile = "Dataverse.Fluent.IntegrationTests.csproj"
if (-not (Test-Path $projectFile)) {
    Write-Host "?" -ForegroundColor Red -NoNewline
    Write-Host " Error: This script must be run from the Dataverse.Fluent.IntegrationTests directory" -ForegroundColor Red
    Write-Host ""
    Write-Host "Current directory: $PWD" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please run:" -ForegroundColor Yellow
    Write-Host "  cd Dataverse.Fluent.IntegrationTests" -ForegroundColor Cyan
    Write-Host "  .\setup-secrets.ps1" -ForegroundColor Cyan
    exit 1
}

Write-Host "This script will help you configure User Secrets for integration tests." -ForegroundColor White
Write-Host "Your credentials will be stored securely on your local machine only." -ForegroundColor White
Write-Host ""

# Prompt for configuration values
Write-Host "Please provide your Dataverse configuration:" -ForegroundColor Yellow
Write-Host ""

$environmentUrl = Read-Host "Environment URL (e.g., https://yourorg.crm9.dynamics.com/)"
$tenantId = Read-Host "Tenant ID"
$clientId = Read-Host "Client ID"
$clientSecret = Read-Host "Client Secret" -AsSecureString
$clientSecretPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($clientSecret)
)

# Validate inputs
if ([string]::IsNullOrWhiteSpace($environmentUrl) -or 
    [string]::IsNullOrWhiteSpace($tenantId) -or 
    [string]::IsNullOrWhiteSpace($clientId) -or 
    [string]::IsNullOrWhiteSpace($clientSecretPlain)) {
    Write-Host ""
    Write-Host "?" -ForegroundColor Red -NoNewline
    Write-Host " Error: All fields are required" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "?" -ForegroundColor Green -NoNewline
Write-Host " Setting User Secrets..." -ForegroundColor Cyan
Write-Host ""

# Set User Secrets
try {
    dotnet user-secrets set "Dataverse:EnvironmentUrl" $environmentUrl | Out-Null
    dotnet user-secrets set "Dataverse:ApiVersion" "v9.2" | Out-Null
    dotnet user-secrets set "Dataverse:AuthMode" "ClientCredentials" | Out-Null
    dotnet user-secrets set "Dataverse:TenantId" $tenantId | Out-Null
    dotnet user-secrets set "Dataverse:ClientId" $clientId | Out-Null
    dotnet user-secrets set "Dataverse:ClientSecret" $clientSecretPlain | Out-Null

    Write-Host "?" -ForegroundColor Green -NoNewline
    Write-Host " User Secrets configured successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Display configured values (without secret)
    Write-Host "Configured values:" -ForegroundColor Yellow
    Write-Host "  Environment URL: $environmentUrl" -ForegroundColor White
    Write-Host "  Tenant ID: $tenantId" -ForegroundColor White
    Write-Host "  Client ID: $clientId" -ForegroundColor White
    Write-Host "  Client Secret: ********" -ForegroundColor White
    Write-Host "  Auth Mode: ClientCredentials" -ForegroundColor White
    Write-Host "  API Version: v9.2" -ForegroundColor White
    Write-Host ""
    
    # Verify
    Write-Host "?" -ForegroundColor Cyan -NoNewline
    Write-Host " Verifying configuration..." -ForegroundColor Cyan
    Write-Host ""
    
    $secrets = dotnet user-secrets list
    if ($LASTEXITCODE -eq 0) {
        Write-Host "?" -ForegroundColor Green -NoNewline
        Write-Host " User Secrets are properly configured!" -ForegroundColor Green
        Write-Host ""
        Write-Host "You can now run the integration tests:" -ForegroundColor Yellow
        Write-Host "  dotnet test" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "To view your secrets at any time, run:" -ForegroundColor Yellow
        Write-Host "  dotnet user-secrets list" -ForegroundColor Cyan
        Write-Host ""
    }
}
catch {
    Write-Host ""
    Write-Host "?" -ForegroundColor Red -NoNewline
    Write-Host " Error setting User Secrets: $_" -ForegroundColor Red
    exit 1
}

# Cleanup
if ($clientSecretPlain) {
    $clientSecretPlain = $null
    [System.GC]::Collect()
}
