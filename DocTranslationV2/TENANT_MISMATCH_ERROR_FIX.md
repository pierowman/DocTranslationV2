# Azure Tenant Mismatch Error - Troubleshooting Guide

## Error Message
```
"Tenant provided in token does not match resource token"
"Token tenant 72f988bf-86f1-41af-91ab-2d7cd011db47 does not match resource tenant."
```

## What This Error Means

This error occurs when Azure credentials from one tenant (organization) are trying to access resources in a different tenant. 

The tenant ID `72f988bf-86f1-41af-91ab-2d7cd011db47` is **Microsoft's internal tenant**, which indicates that `DefaultAzureCredential` was attempting to use Microsoft employee credentials (likely from Visual Studio or Azure CLI) instead of your organization's credentials.

## Root Cause

The application was using two different authentication methods:

1. **Blob Storage**: `ClientSecretCredential` with your organization's TenantId
2. **Translation Service**: `DefaultAzureCredential` which picked up Microsoft tenant credentials

When the Translation Service tried to access your Blob Storage, it failed because:
- Translation Service authenticated with Microsoft tenant credentials
- Blob Storage resources belong to your organization's tenant
- Azure doesn't allow cross-tenant access without explicit configuration

## The Fix

### What Was Changed

The `CredentialService.cs` was updated to use **the same `ClientSecretCredential`** for both services:

**Before** (incorrect):
```csharp
// Blob Storage - correct
_blobCredential = new ClientSecretCredential(
    settings.TenantId,    // Your tenant
    settings.ClientId,
    settings.ClientSecret);

// Translation Service - WRONG (picks up Microsoft tenant)
_translationCredential = new DefaultAzureCredential();
```

**After** (correct):
```csharp
// Both services use the same tenant credentials
_blobCredential = new ClientSecretCredential(
    settings.TenantId,
    settings.ClientId,
    settings.ClientSecret);

_translationCredential = new ClientSecretCredential(
    settings.TenantId,    // Same tenant as blob storage
    settings.ClientId,
    settings.ClientSecret);
```

## Verify the Fix

### Step 1: Check Configuration

Run the verification script:
```powershell
.\verify-user-secrets.ps1
```

Ensure these are set:
- `AzureBlobStorage:TenantId` - Your organization's tenant ID
- `AzureBlobStorage:ClientId` - Your app registration's client ID
- `AzureBlobStorage:ClientSecret` - Your app's client secret

### Step 2: Verify Tenant ID

Get your correct tenant ID:
```powershell
# Using Azure CLI
az account show --query tenantId -o tsv
```

The output should **NOT** be `72f988bf-86f1-41af-91ab-2d7cd011db47`.

If it is, you're logged into Microsoft's tenant. Switch to your organization:
```powershell
# List available tenants
az account list --query "[].{Name:name, TenantId:tenantId}" -o table

# Switch to your organization's tenant
az account set --subscription "YOUR_SUBSCRIPTION_NAME"
```

### Step 3: Update User Secrets

Set your correct tenant ID:
```powershell
cd DocTranslationV2
dotnet user-secrets set "AzureBlobStorage:TenantId" "YOUR_TENANT_ID"
```

### Step 4: Verify App Registration Permissions

In Azure Portal:

1. Go to **Azure Active Directory** ? **App registrations**
2. Find your app registration
3. Go to **API permissions**
4. Ensure it has:
   - **Azure Storage** ? `user_impersonation`
   - **Azure Cognitive Services** ? `user_impersonation` (or appropriate scope)

5. Click **"Grant admin consent"** if needed

### Step 5: Test the Application

Run the application and check console output:
```
info: DocTranslationV2.Services.CredentialService[0]
      Creating ClientSecretCredential for Blob Storage with TenantId: YOUR_TENANT_ID, ClientId: YOUR_CLIENT_ID
info: DocTranslationV2.Services.CredentialService[0]
      Creating ClientSecretCredential for Translation Service with TenantId: YOUR_TENANT_ID, ClientId: YOUR_CLIENT_ID
```

Both should show **the same TenantId**.

## Understanding DefaultAzureCredential

### What It Does

`DefaultAzureCredential` tries multiple authentication methods in order:
1. Environment variables
2. Managed Identity
3. Visual Studio
4. Azure CLI
5. Azure PowerShell
6. Interactive browser

### Why It Failed

In your case, it picked up credentials from **Visual Studio or Azure CLI** that were authenticated to Microsoft's tenant (if you're a Microsoft employee or testing with Microsoft credentials).

### When to Use It

`DefaultAzureCredential` is useful for:
- ? Production environments with Managed Identity
- ? Local development when logged into the correct tenant
- ? CI/CD pipelines with proper environment variables

**Don't use it when:**
- ? You need to specify a particular tenant
- ? You're using App Registration with client secrets
- ? You need consistent authentication across services

## Alternative Solutions

### Solution 1: Use Managed Identity (Production)

For production environments, use Managed Identity:

```csharp
// In production
if (environment.IsProduction())
{
    _translationCredential = new ManagedIdentityCredential();
}
else
{
    _translationCredential = new ClientSecretCredential(...);
}
```

### Solution 2: Environment-Specific Configuration

Configure different credentials per environment:

```csharp
var options = new DefaultAzureCredentialOptions
{
    TenantId = settings.TenantId,  // Force specific tenant
    ExcludeVisualStudioCredential = true,
    ExcludeAzureCliCredential = true
};
_translationCredential = new DefaultAzureCredential(options);
```

### Solution 3: Separate Configuration

Add separate translation service configuration:

```json
{
  "AzureBlobStorage": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-secret"
  },
  "AzureTranslation": {
    "TenantId": "your-tenant-id",  // Add these
    "ClientId": "your-client-id",
    "ClientSecret": "your-secret"
  }
}
```

## Common Scenarios

### Scenario 1: Microsoft Employee

If you're a Microsoft employee:
- Your Azure CLI/VS might default to Microsoft tenant
- Always specify tenant when logging in:
  ```powershell
  az login --tenant YOUR_ORGANIZATION_TENANT_ID
  ```

### Scenario 2: Multi-Tenant Organization

If your organization has multiple tenants:
- Ensure all resources are in the same tenant
- Or configure cross-tenant access explicitly
- Use the tenant where your Translation Service resides

### Scenario 3: Azure Government or China

For special Azure clouds:
```csharp
var credential = new ClientSecretCredential(
    tenantId,
    clientId,
    clientSecret,
    new TokenCredentialOptions
    {
        AuthorityHost = AzureAuthorityHosts.AzureGovernment
    });
```

## Verification Checklist

After applying the fix:

- [ ] Application builds successfully
- [ ] Console shows same TenantId for both services
- [ ] No tenant mismatch errors in logs
- [ ] Can upload files to blob storage
- [ ] Translation service can start jobs
- [ ] Translation service can access blob storage
- [ ] Downloaded files work correctly

## Additional Resources

- [Azure Identity Documentation](https://docs.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme)
- [DefaultAzureCredential](https://docs.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential)
- [ClientSecretCredential](https://docs.microsoft.com/en-us/dotnet/api/azure.identity.clientsecretcredential)
- [Troubleshooting Authentication](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-troubleshoot-app-access)

## Still Having Issues?

If you still see the error:

1. **Clear cached credentials**:
   ```powershell
   # Clear Azure CLI cache
   az account clear
   az login --tenant YOUR_TENANT_ID
   
   # Clear VS credentials
   # Tools ? Options ? Azure Service Authentication ? Re-authenticate
   ```

2. **Check application logs** for detailed error messages

3. **Verify network access** to Azure endpoints

4. **Test with minimal app** to isolate the issue

5. **Check Azure Portal** ? App Registration ? Authentication logs
