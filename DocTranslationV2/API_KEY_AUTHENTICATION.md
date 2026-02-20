# API Key Authentication for Azure Translation Service

## Overview

The application has been updated to use **API Key (Subscription Key)** authentication for Azure Translation Service instead of Service Principal authentication. This simplifies the authentication process and is the recommended approach for Azure Cognitive Services.

## Changes Made

### 1. Configuration Model Updates

**File:** `Models/TranslationConfiguration.cs`

Added `SubscriptionKey` property to `AzureTranslationSettings`:
```csharp
public class AzureTranslationSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string SubscriptionKey { get; set; } = string.Empty;  // ? NEW
    // ...existing properties...
}
```

### 2. Configuration Files Updated

**Files:** `appsettings.json` and `appsettings.Development.json`

Added `SubscriptionKey` field:
```json
{
  "AzureTranslation": {
    "Endpoint": "https://translationcbo.cognitiveservices.azure.com/",
    "Region": "eastus2",
    "SubscriptionKey": ""  // ? Add your key here
  }
}
```

### 3. Credential Service Changes

**File:** `Services/CredentialService.cs`

Changed Translation Service credential type from `ClientSecretCredential` to `AzureKeyCredential`:

**Before:**
```csharp
public interface ICredentialService
{
    TokenCredential GetBlobStorageCredential();
    TokenCredential GetTranslationServiceCredential();  // ? Returns TokenCredential
}
```

**After:**
```csharp
public interface ICredentialService
{
    TokenCredential GetBlobStorageCredential();
    AzureKeyCredential GetTranslationServiceCredential();  // ? Returns AzureKeyCredential
}
```

The implementation now creates an `AzureKeyCredential` using the subscription key:
```csharp
_translationCredential = new Lazy<AzureKeyCredential>(() =>
{
    if (string.IsNullOrWhiteSpace(_translationSettings.SubscriptionKey))
    {
        throw new InvalidOperationException(
            "AzureTranslation:SubscriptionKey is required but not configured.");
    }
    return new AzureKeyCredential(_translationSettings.SubscriptionKey);
});
```

## How to Get Your Azure Translation Service API Key

### Option 1: Azure Portal

1. Navigate to the [Azure Portal](https://portal.azure.com)
2. Go to your **Azure Translator** resource (e.g., `translationcbo`)
3. In the left menu, click **Keys and Endpoint**
4. Copy either **KEY 1** or **KEY 2**

### Option 2: Azure CLI

```bash
# Get keys for your translation service
az cognitiveservices account keys list \
  --name translationcbo \
  --resource-group <your-resource-group>
```

## Configuration Options

### Option 1: User Secrets (Recommended for Development)

```bash
cd DocTranslationV2

dotnet user-secrets set "AzureTranslation:SubscriptionKey" "YOUR_API_KEY_HERE"
```

**Advantages:**
- ? Keeps secrets out of source control
- ? Separate keys per developer
- ? Easy to update

### Option 2: appsettings.Development.json (Local Only - Less Secure)

Edit `appsettings.Development.json`:
```json
{
  "AzureTranslation": {
    "Endpoint": "https://translationcbo.cognitiveservices.azure.com/",
    "Region": "eastus2",
    "SubscriptionKey": "YOUR_API_KEY_HERE"
  }
}
```

?? **WARNING:** Do NOT commit this file with your actual key to source control!

### Option 3: Azure App Service Configuration (Production)

Set as an App Setting in Azure Portal or via CLI:

```bash
az webapp config appsettings set \
  --name <your-app-name> \
  --resource-group <your-resource-group> \
  --settings AzureTranslation__SubscriptionKey="YOUR_API_KEY_HERE"
```

## Authentication Flow

### Translation Service (API Key) ?
```
Request ? DocumentTranslationService
    ?
    Uses: credentialService.GetTranslationServiceCredential()
    ?
    Returns: AzureKeyCredential(subscriptionKey)
    ?
    Azure SDK adds header: Ocp-Apim-Subscription-Key: <your-key>
    ?
    Azure Translation Service validates key
```

### Blob Storage (Service Principal) ?
```
Request ? BlobStorageService
    ?
    Uses: credentialService.GetBlobStorageCredential()
    ?
    Returns: ClientSecretCredential (TenantId, ClientId, ClientSecret)
    ?
    Azure SDK requests OAuth token from Azure AD
    ?
    Token used for Storage Account access
```

## Benefits of API Key Authentication

### For Translation Service
- ? **Simpler setup** - No need for App Registration or Service Principal
- ? **Faster authentication** - No OAuth token exchange required
- ? **Easier debugging** - Direct key validation
- ? **Standard approach** - Recommended by Microsoft for Cognitive Services
- ? **Key rotation** - Easy to regenerate and update keys

### Blob Storage Still Uses Service Principal Because:
- Blob Storage requires role-based access control (RBAC)
- Translation Service needs to access blobs using its own managed identity
- API keys are not supported for Storage Account access in this scenario

## Validation

The application will validate the configuration on startup:

```csharp
if (string.IsNullOrWhiteSpace(_translationSettings.SubscriptionKey))
{
    throw new InvalidOperationException(
        "AzureTranslation:SubscriptionKey is required but not configured. " +
        "Please set it in user secrets or appsettings.json");
}
```

## Security Best Practices

1. ? **Never commit API keys to source control**
2. ? **Use User Secrets for local development**
3. ? **Use Azure App Service Configuration for production**
4. ? **Rotate keys regularly** (every 3-6 months)
5. ? **Use separate keys for development and production**
6. ? **Monitor key usage** in Azure Portal
7. ? **Regenerate keys immediately if compromised**

## Troubleshooting

### Error: "SubscriptionKey is required but not configured"

**Solution:** Set the key using one of the configuration options above.

### Error: "Access denied" or "401 Unauthorized"

**Causes:**
- Invalid or expired API key
- Key not configured correctly
- Wrong region specified

**Solution:**
1. Verify the key is correct in Azure Portal
2. Check that the region matches your resource
3. Try regenerating the key

### Error: "The subscription key is not valid"

**Solution:**
- Copy the key directly from Azure Portal
- Ensure no extra spaces or characters
- Try using KEY 2 if KEY 1 doesn't work

## Complete Configuration Checklist

- [ ] Get API Key from Azure Portal (Keys and Endpoint)
- [ ] Set `AzureTranslation:SubscriptionKey` in user secrets or appsettings
- [ ] Verify `AzureTranslation:Endpoint` is correct
- [ ] Verify `AzureTranslation:Region` matches your resource
- [ ] Test translation functionality
- [ ] Remove any old Service Principal settings for translation (if they existed)

## Example Complete Configuration

### User Secrets (Development)
```bash
dotnet user-secrets set "AzureTranslation:Endpoint" "https://translationcbo.cognitiveservices.azure.com/"
dotnet user-secrets set "AzureTranslation:Region" "eastus2"
dotnet user-secrets set "AzureTranslation:SubscriptionKey" "abc123...xyz"

# Blob Storage still needs Service Principal
dotnet user-secrets set "AzureBlobStorage:AccountName" "doctranslationstoragecbo"
dotnet user-secrets set "AzureBlobStorage:TenantId" "f4ce5cd6-..."
dotnet user-secrets set "AzureBlobStorage:ClientId" "7a9961cd-..."
dotnet user-secrets set "AzureBlobStorage:ClientSecret" "p7d8Q~..."
```

### Azure App Service (Production)
```bash
az webapp config appsettings set --name myapp --resource-group myrg --settings \
  AzureTranslation__Endpoint="https://translationcbo.cognitiveservices.azure.com/" \
  AzureTranslation__Region="eastus2" \
  AzureTranslation__SubscriptionKey="abc123...xyz" \
  AzureBlobStorage__AccountName="doctranslationstoragecbo" \
  AzureBlobStorage__TenantId="f4ce5cd6-..." \
  AzureBlobStorage__ClientId="7a9961cd-..." \
  AzureBlobStorage__ClientSecret="p7d8Q~..."
```

---

## Summary

The application now uses:
- **Azure Translation Service**: API Key authentication (simpler, recommended)
- **Azure Blob Storage**: Service Principal authentication (required for RBAC)

This hybrid approach provides the best balance of simplicity and security for your document translation application.
