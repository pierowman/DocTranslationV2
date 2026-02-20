# Quick Fix: User Secrets Not Loading

## Problem
TenantId and other Azure Blob Storage configuration values aren't being picked up from user secrets.

## Immediate Actions

### Step 1: Verify User Secrets
Run the verification script:
```powershell
.\verify-user-secrets.ps1
```

### Step 2: Check the Output
When you run the application, you'll now see diagnostic output:
```
AzureBlobStorage Configuration:
  AccountName: mystorageaccount
  TenantId: <configured> ?
  ClientId: <configured> ?
  ClientSecret: <configured> ?
  ContainerName: translations
```

If you see `<not set>` instead of `<configured>`, the secrets aren't loading.

### Step 3: Set Missing Secrets

**Option A - Visual Studio (Easiest)**:
1. Right-click `DocTranslationV2` project
2. Select "Manage User Secrets"
3. Add this JSON:
```json
{
  "AzureBlobStorage": {
    "AccountName": "your-storage-account-name",
    "TenantId": "your-tenant-id-guid",
    "ClientId": "your-client-id-guid",
    "ClientSecret": "your-client-secret-value"
  }
}
```

**Option B - Command Line**:
```powershell
cd DocTranslationV2
dotnet user-secrets set "AzureBlobStorage:TenantId" "your-tenant-id"
dotnet user-secrets set "AzureBlobStorage:ClientId" "your-client-id"
dotnet user-secrets set "AzureBlobStorage:ClientSecret" "your-secret"
dotnet user-secrets set "AzureBlobStorage:AccountName" "your-storage-account"
```

## What Was Fixed

### 1. Configuration Binding
Changed from `.Get<>()` to `.Bind()` for better user secrets support:
```csharp
// Before - may not pick up user secrets correctly
config.AzureBlobStorage = builder.Configuration.GetSection("AzureBlobStorage").Get<AzureBlobStorageSettings>() ?? new();

// After - properly binds user secrets
builder.Configuration.GetSection("AzureBlobStorage").Bind(config.AzureBlobStorage);
```

### 2. Diagnostic Logging
Added startup logging to show which configuration sources are loaded and what values are detected.

### 3. Better Error Messages
The `CredentialService` now provides clear error messages if secrets are missing:
```
System.InvalidOperationException: AzureBlobStorage:TenantId is required but not configured. 
Please set it in user secrets or appsettings.json
```

## Common Issues

### Issue: Running in Docker
User secrets DON'T work in Docker containers. Use environment variables instead:

```powershell
# Set environment variables for Docker
$env:AzureBlobStorage__TenantId="your-tenant-id"
$env:AzureBlobStorage__ClientId="your-client-id"
$env:AzureBlobStorage__ClientSecret="your-secret"
$env:AzureBlobStorage__AccountName="your-storage-account"

# Note: Use double underscores (__) for nested settings
```

Or update `docker-compose.override.yml`:
```yaml
services:
  web:
    environment:
      - AzureBlobStorage__TenantId=${AZURE_TENANT_ID}
      - AzureBlobStorage__ClientId=${AZURE_CLIENT_ID}
      - AzureBlobStorage__ClientSecret=${AZURE_CLIENT_SECRET}
      - AzureBlobStorage__AccountName=${AZURE_STORAGE_ACCOUNT}
```

### Issue: Secrets File Location
Windows: `%APPDATA%\Microsoft\UserSecrets\062188b3-fd03-4bf4-8ead-509823a1ffed\secrets.json`

You can open this file directly and edit it if needed.

### Issue: Wrong Format
? **Wrong** (don't use dots in keys):
```json
{
  "AzureBlobStorage.TenantId": "value"
}
```

? **Correct** (use nested structure):
```json
{
  "AzureBlobStorage": {
    "TenantId": "value"
  }
}
```

## Verification Checklist

- [ ] Run `.\verify-user-secrets.ps1` - all checks pass
- [ ] Run the application - see configuration diagnostic output
- [ ] All values show `<configured>` instead of `<not set>`
- [ ] No errors about missing TenantId/ClientId/ClientSecret
- [ ] Application successfully connects to blob storage

## Getting Help

If secrets still aren't loading:

1. **Check the Output Window** in Visual Studio when running
2. **Look for the configuration diagnostic output** at startup
3. **Review the full troubleshooting guide**: `USER_SECRETS_TROUBLESHOOTING.md`
4. **Verify environment**: Make sure you're running in Development mode (not Docker)

## Files Modified

? `Program.cs` - Fixed configuration binding and added diagnostics
? `CredentialService.cs` - Added validation and error messages
? `verify-user-secrets.ps1` - Script to check your configuration
? `USER_SECRETS_TROUBLESHOOTING.md` - Complete troubleshooting guide
