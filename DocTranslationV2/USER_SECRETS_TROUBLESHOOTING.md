# Troubleshooting User Secrets for Azure Blob Storage

## Problem
The TenantId and other Azure Blob Storage configuration values aren't being picked up from user secrets.

## Quick Diagnostic Steps

### Step 1: Verify User Secrets Path

Run this command to see your secrets.json location:
```powershell
dotnet user-secrets list --project DocTranslationV2
```

### Step 2: Check if Secrets are Set

The output should show your configuration. If empty, secrets need to be added.

### Step 3: View Configuration on Startup

When you run the application in Development mode, it will now log the configuration status:
```
Configuration Sources:
  - JsonConfigurationProvider (appsettings.json)
  - JsonConfigurationProvider (appsettings.Development.json)
  - UserSecretsConfigurationProvider
  
AzureBlobStorage Configuration:
  AccountName: <your-account>
  TenantId: <configured> or <not set>
  ClientId: <configured> or <not set>
  ClientSecret: <configured> or <not set>
  ContainerName: translations
```

## Setting User Secrets

### Option 1: Using Visual Studio

1. **Right-click** on the `DocTranslationV2` project
2. Select **"Manage User Secrets"**
3. Add your configuration:

```json
{
  "AzureBlobStorage": {
    "AccountName": "yourstorageaccount",
    "TenantId": "your-tenant-id-guid",
    "ClientId": "your-client-id-guid",
    "ClientSecret": "your-client-secret-value",
    "ContainerName": "translations"
  },
  "AzureTranslation": {
    "Endpoint": "https://your-translator.cognitiveservices.azure.com/",
    "Region": "eastus"
  },
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=your-key;..."
  }
}
```

### Option 2: Using CLI

```powershell
# Navigate to project directory
cd DocTranslationV2

# Set each secret
dotnet user-secrets set "AzureBlobStorage:AccountName" "yourstorageaccount"
dotnet user-secrets set "AzureBlobStorage:TenantId" "your-tenant-id-guid"
dotnet user-secrets set "AzureBlobStorage:ClientId" "your-client-id-guid"
dotnet user-secrets set "AzureBlobStorage:ClientSecret" "your-client-secret-value"
dotnet user-secrets set "AzureBlobStorage:ContainerName" "translations"

dotnet user-secrets set "AzureTranslation:Endpoint" "https://your-translator.cognitiveservices.azure.com/"
dotnet user-secrets set "AzureTranslation:Region" "eastus"
```

### Option 3: Manually Edit secrets.json

1. Find your secrets file location:
   - Windows: `%APPDATA%\Microsoft\UserSecrets\062188b3-fd03-4bf4-8ead-509823a1ffed\secrets.json`
   - Linux/Mac: `~/.microsoft/usersecrets/062188b3-fd03-4bf4-8ead-509823a1ffed/secrets.json`

2. Edit the file and add the configuration (same JSON structure as Option 1)

## Common Issues

### Issue 1: User Secrets Not Loaded in Docker

**Problem**: User secrets are only loaded when running locally, not in Docker containers.

**Solution**: For Docker debugging, use environment variables or mount secrets:

```yaml
# docker-compose.override.yml
services:
  web:
    environment:
      - AzureBlobStorage__TenantId=${AZURE_TENANT_ID}
      - AzureBlobStorage__ClientId=${AZURE_CLIENT_ID}
      - AzureBlobStorage__ClientSecret=${AZURE_CLIENT_SECRET}
      - AzureBlobStorage__AccountName=${AZURE_STORAGE_ACCOUNT}
```

Or set environment variables before running:
```powershell
$env:AzureBlobStorage__TenantId="your-tenant-id"
$env:AzureBlobStorage__ClientId="your-client-id"
$env:AzureBlobStorage__ClientSecret="your-client-secret"
$env:AzureBlobStorage__AccountName="your-storage-account"
```

### Issue 2: Wrong Secret Path Format

**Incorrect**:
```json
{
  "AzureBlobStorage.TenantId": "value"  // Wrong - uses dot
}
```

**Correct**:
```json
{
  "AzureBlobStorage": {
    "TenantId": "value"  // Correct - uses nested structure
  }
}
```

Or using CLI (colon separators):
```powershell
dotnet user-secrets set "AzureBlobStorage:TenantId" "value"
```

### Issue 3: Multiple Projects

If you have multiple projects in the solution, make sure you're setting secrets for the correct project:

```powershell
dotnet user-secrets set "key" "value" --project DocTranslationV2
```

### Issue 4: UserSecretsId Missing

Check that `DocTranslationV2.csproj` contains:
```xml
<PropertyGroup>
  <UserSecretsId>062188b3-fd03-4bf4-8ead-509823a1ffed</UserSecretsId>
</PropertyGroup>
```

If missing, initialize user secrets:
```powershell
dotnet user-secrets init --project DocTranslationV2
```

### Issue 5: Configuration Binding Issues

The updated `Program.cs` now uses `.Bind()` instead of `.Get()` which better handles nested properties:

**Old (may not work with user secrets)**:
```csharp
config.AzureBlobStorage = builder.Configuration.GetSection("AzureBlobStorage").Get<AzureBlobStorageSettings>() ?? new();
```

**New (works correctly)**:
```csharp
builder.Configuration.GetSection("AzureBlobStorage").Bind(config.AzureBlobStorage);
```

## Verification

After setting secrets, run the application and check:

1. **Console Output**: Look for the diagnostic output showing configuration sources and values
2. **Error Messages**: The `CredentialService` will now throw clear errors if values are missing
3. **Application Insights**: Check if credential creation succeeds

### Expected Console Output (Success):
```
Configuration Sources:
  - JsonConfigurationProvider
  - JsonConfigurationProvider
  - UserSecretsConfigurationProvider
  - EnvironmentVariablesConfigurationProvider

AzureBlobStorage Configuration:
  AccountName: mystorageaccount
  TenantId: <configured>
  ClientId: <configured>
  ClientSecret: <configured>
  ContainerName: translations

info: DocTranslationV2.Services.CredentialService[0]
      Initializing blob storage credential
info: DocTranslationV2.Services.CredentialService[0]
      Creating ClientSecretCredential with TenantId: 12345678-1234-..., ClientId: 87654321-4321-...
```

### Expected Console Output (Failure):
```
AzureBlobStorage Configuration:
  AccountName: 
  TenantId: <not set>
  ClientId: <not set>
  ClientSecret: <not set>
  ContainerName: translations

fail: DocTranslationV2.Services.CredentialService[0]
      TenantId is not configured for blob storage
      System.InvalidOperationException: AzureBlobStorage:TenantId is required but not configured...
```

## Environment-Specific Configuration

### Development (User Secrets)
- Use for local development
- Never commit to source control
- Automatically loaded in Development environment

### Staging/Production (Azure Key Vault or App Settings)
- Use Azure Key Vault for production
- Or use App Service Application Settings
- Configure in Azure Portal

### Docker (Environment Variables)
- Use docker-compose.override.yml
- Or set environment variables
- Format: `Section__SubSection__Key`

## Testing Your Configuration

After setting secrets, test with this PowerShell script:

```powershell
# Test if secrets are readable by the app
cd DocTranslationV2
dotnet user-secrets list

# Expected output should show your secrets
# AzureBlobStorage:AccountName = yourstorageaccount
# AzureBlobStorage:TenantId = your-tenant-id
# etc.
```

## Getting Azure Credentials

If you need to find your Azure credentials:

### TenantId
```powershell
az account show --query tenantId -o tsv
```

### ClientId and ClientSecret
1. Go to Azure Portal ? Azure Active Directory ? App registrations
2. Find or create your app registration
3. **ClientId**: Copy the "Application (client) ID"
4. **ClientSecret**: Go to "Certificates & secrets" ? Create new client secret

### Storage Account Name
```powershell
az storage account list --query "[].{Name:name}" -o table
```

## Additional Resources

- [Safe storage of app secrets in development](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [Configuration in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [Azure Key Vault configuration provider](https://docs.microsoft.com/en-us/aspnet/core/security/key-vault-configuration)
