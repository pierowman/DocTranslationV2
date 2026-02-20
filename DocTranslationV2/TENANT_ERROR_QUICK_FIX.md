# Quick Fix: Tenant Mismatch Error

## Error
```
"Token tenant 72f988bf-86f1-41af-91ab-2d7cd011db47 does not match resource tenant."
```

## Where It's Coming From
The error is coming from the **Azure Translation Service** in `DocumentTranslationService.cs` when it tries to access Blob Storage.

## Root Cause
- **Blob Storage** was using your organization's tenant credentials ?
- **Translation Service** was using `DefaultAzureCredential` which picked up Microsoft tenant credentials ?
- Both services must use the **same tenant**

## What Was Fixed
Changed `CredentialService.cs` to use **`ClientSecretCredential`** for both services instead of `DefaultAzureCredential`.

**Before**:
```csharp
// Translation Service - WRONG
_translationCredential = new DefaultAzureCredential();
```

**After**:
```csharp
// Translation Service - CORRECT
_translationCredential = new ClientSecretCredential(
    settings.TenantId,     // Same tenant as Blob Storage
    settings.ClientId,
    settings.ClientSecret);
```

## Verify Your Configuration

### Step 1: Check Tenant ID
```powershell
# Get your tenant ID
az account show --query tenantId -o tsv
```

Should **NOT** be `72f988bf-86f1-41af-91ab-2d7cd011db47` (Microsoft's tenant).

### Step 2: Verify User Secrets
```powershell
.\verify-user-secrets.ps1
```

Ensure these match your organization:
- `AzureBlobStorage:TenantId` = Your tenant ID
- `AzureBlobStorage:ClientId` = Your app registration
- `AzureBlobStorage:ClientSecret` = Your secret

### Step 3: Run Application
Check console output shows the same tenant for both services:
```
Creating ClientSecretCredential for Blob Storage with TenantId: YOUR_TENANT_ID
Creating ClientSecretCredential for Translation Service with TenantId: YOUR_TENANT_ID
```

## If Still Not Working

### You're Using Wrong Azure Account

If you're a Microsoft employee or have multiple Azure accounts:

```powershell
# List all tenants
az account list -o table

# Login to correct tenant
az login --tenant YOUR_TENANT_ID

# Set correct subscription
az account set --subscription "YOUR_SUBSCRIPTION"
```

### Clear Cached Credentials

```powershell
# Clear Azure CLI
az account clear
az login --tenant YOUR_TENANT_ID

# In Visual Studio
# Tools ? Options ? Azure Service Authentication ? Clear/Re-authenticate
```

### Verify App Registration

1. Go to **Azure Portal** ? **Azure Active Directory** ? **App registrations**
2. Find your app
3. Note the **Directory (tenant) ID** - this should match your user secret
4. Go to **API permissions** and ensure:
   - Azure Storage permissions granted
   - Cognitive Services permissions granted
   - **Admin consent granted** (click the button if not)

## Quick Test

Try uploading a small file for translation. If you see:
- ? File uploads successfully ? Blob Storage authentication works
- ? Translation starts ? Both services authentication works
- ? No tenant error ? Fix successful!

## Files Modified

? `CredentialService.cs` - Fixed to use same tenant for both services
? `TENANT_MISMATCH_ERROR_FIX.md` - Detailed troubleshooting guide

## Need More Help?

See the detailed guide: `TENANT_MISMATCH_ERROR_FIX.md`

Key points:
- Both Blob Storage and Translation Service must use same tenant
- `DefaultAzureCredential` picks up credentials automatically (can be wrong tenant)
- `ClientSecretCredential` explicitly specifies tenant (correct for this app)
- The tenant ID `72f988bf-86f1-41af-91ab-2d7cd011db47` is Microsoft's internal tenant
