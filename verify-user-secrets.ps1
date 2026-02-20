# Verify User Secrets Configuration
# This script checks if your user secrets are properly configured

Write-Host "=== User Secrets Configuration Checker ===" -ForegroundColor Cyan
Write-Host ""

$projectPath = "DocTranslationV2"
$projectFile = "$projectPath\DocTranslationV2.csproj"

# Check if project exists
if (-not (Test-Path $projectFile)) {
    Write-Host "? Project file not found: $projectFile" -ForegroundColor Red
    Write-Host "   Please run this script from the solution root directory." -ForegroundColor Yellow
    exit 1
}

Write-Host "? Project file found" -ForegroundColor Green
Write-Host ""

# Check UserSecretsId in project file
Write-Host "Checking UserSecretsId..." -ForegroundColor Cyan
$projectContent = Get-Content $projectFile -Raw
if ($projectContent -match '<UserSecretsId>(.*?)</UserSecretsId>') {
    $userSecretsId = $matches[1]
    Write-Host "? UserSecretsId found: $userSecretsId" -ForegroundColor Green
} else {
    Write-Host "? UserSecretsId not found in project file" -ForegroundColor Red
    Write-Host "   Run: dotnet user-secrets init --project $projectPath" -ForegroundColor Yellow
    exit 1
}
Write-Host ""

# List user secrets
Write-Host "Reading user secrets..." -ForegroundColor Cyan
$secretsList = dotnet user-secrets list --project $projectPath 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "? Failed to read user secrets" -ForegroundColor Red
    Write-Host $secretsList -ForegroundColor Red
    exit 1
}

if ($secretsList -match "No secrets configured") {
    Write-Host "??  No secrets configured" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To set secrets, run:" -ForegroundColor Cyan
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:TenantId`" `"your-value`" --project $projectPath" -ForegroundColor White
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:ClientId`" `"your-value`" --project $projectPath" -ForegroundColor White
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:ClientSecret`" `"your-value`" --project $projectPath" -ForegroundColor White
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:AccountName`" `"your-value`" --project $projectPath" -ForegroundColor White
    Write-Host ""
    Write-Host "Or right-click project in Visual Studio ? Manage User Secrets" -ForegroundColor Cyan
    exit 0
}

Write-Host "Current secrets:" -ForegroundColor Green
Write-Host $secretsList -ForegroundColor White
Write-Host ""

# Check for required Azure Blob Storage settings
Write-Host "Checking required settings..." -ForegroundColor Cyan
$requiredSettings = @(
    "AzureBlobStorage:TenantId",
    "AzureBlobStorage:ClientId",
    "AzureBlobStorage:ClientSecret",
    "AzureBlobStorage:AccountName"
)

$allConfigured = $true
foreach ($setting in $requiredSettings) {
    if ($secretsList -match [regex]::Escape($setting)) {
        Write-Host "  ? $setting" -ForegroundColor Green
    } else {
        Write-Host "  ? $setting (not configured)" -ForegroundColor Red
        $allConfigured = $false
    }
}

Write-Host ""

if ($allConfigured) {
    Write-Host "? All required blob storage settings are configured!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Run the application" -ForegroundColor White
    Write-Host "  2. Check console output for 'AzureBlobStorage Configuration'" -ForegroundColor White
    Write-Host "  3. Verify '<configured>' appears for TenantId, ClientId, and ClientSecret" -ForegroundColor White
} else {
    Write-Host "??  Some required settings are missing" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To set missing secrets, run:" -ForegroundColor Cyan
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:TenantId`" `"your-tenant-id`" --project $projectPath" -ForegroundColor White
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:ClientId`" `"your-client-id`" --project $projectPath" -ForegroundColor White
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:ClientSecret`" `"your-secret`" --project $projectPath" -ForegroundColor White
    Write-Host "  dotnet user-secrets set `"AzureBlobStorage:AccountName`" `"your-storage-account`" --project $projectPath" -ForegroundColor White
}

Write-Host ""
Write-Host "=== Check Complete ===" -ForegroundColor Cyan
