# InvalidDocumentAccessLevel Error - Complete Fix Guide

## The Error You're Seeing

```json
{
  "code": "InvalidRequest",
  "message": "Cannot access source document location with the current permissions.",
  "target": "Operation",
  "innerError": {
    "code": "InvalidDocumentAccessLevel",
    "message": "Cannot access source document location with the current permissions."
  }
}
```

## What This Error Means

**`InvalidDocumentAccessLevel`** is a specific error from Azure Translation Service that means:

? **The Translation Service cannot authenticate to your blob storage**

This happens when:
1. The Translation Service's **managed identity is not enabled**
2. The managed identity **doesn't have permission** to read/write blobs
3. The **role assignment hasn't propagated yet** (takes 5-10 minutes after assignment)
4. You're using **SAS tokens** but they're invalid or expired

## Two Solutions

You can fix this in two ways:

### ? **Solution 1: Managed Identity (Recommended)**
- More secure
- No tokens to manage or expire
- Automatic credential rotation by Azure
- Requires Azure portal configuration

### ? **Solution 2: SAS Tokens (Quick Fix)**
- Works immediately
- No Azure portal configuration needed
- Tokens expire after set time
- Less secure (tokens in URLs)

---

## Solution 1: Enable Managed Identity (Recommended)

This is what your code currently expects and the recommended approach.

### Step 1: Enable Managed Identity on Translation Service

#### Using Azure Portal:
1. Go to **Azure Portal** ? Find your **Translation Service** (Cognitive Services)
2. Click **Identity** in the left menu
3. Under **System assigned** tab:
   - Toggle **Status** to **On**
   - Click **Save**
   - Wait for confirmation
4. **Copy the Object (principal) ID** that appears

#### Using Azure CLI:
```powershell
# Enable managed identity
az cognitiveservices account identity assign `
    --name YOUR_TRANSLATION_SERVICE_NAME `
    --resource-group YOUR_RESOURCE_GROUP

# Get the principal ID (save this!)
$principalId = az cognitiveservices account identity show `
    --name YOUR_TRANSLATION_SERVICE_NAME `
    --resource-group YOUR_RESOURCE_GROUP `
    --query principalId -o tsv

Write-Host "Translation Service Principal ID: $principalId"
```

### Step 2: Grant Storage Permissions

The Translation Service needs **"Storage Blob Data Contributor"** role.

#### Using Azure Portal:
1. Go to your **Storage Account** (doctranslationstoragecbo)
2. Click **Access Control (IAM)**
3. Click **+ Add** ? **Add role assignment**
4. **Role tab**: Search for and select **"Storage Blob Data Contributor"**
5. Click **Next**
6. **Members tab**: 
   - Click **+ Select members**
   - Search for your Translation Service name
   - Select it and click **Select**
7. Click **Review + assign** twice

#### Using Azure CLI:
```powershell
# Set your values
$translationServiceName = "YOUR_TRANSLATION_SERVICE_NAME"
$storageAccountName = "doctranslationstoragecbo"
$resourceGroup = "YOUR_RESOURCE_GROUP"
$subscriptionId = "YOUR_SUBSCRIPTION_ID"

# Get the principal ID (from Step 1)
$principalId = az cognitiveservices account identity show `
    --name $translationServiceName `
    --resource-group $resourceGroup `
    --query principalId -o tsv

# Grant the role
az role assignment create `
    --role "Storage Blob Data Contributor" `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccountName"

Write-Host "? Role assigned successfully!"
Write-Host "??  Wait 5-10 minutes for permissions to propagate"
```

### Step 3: Wait for Propagation

?? **Important:** After assigning the role, **wait 5-10 minutes** before testing.

### Step 4: Verify the Setup

```powershell
# List all role assignments for your Translation Service
az role assignment list `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccountName" `
    --output table
```

Expected output:
```
PrincipalName              Role                           Scope
-----------------------    ----------------------------   ----
YOUR_TRANSLATION_SERVICE   Storage Blob Data Contributor  .../storageAccounts/doctranslationstoragecbo
```

### Step 5: Test

1. Restart your application (to get fresh credentials)
2. Submit a translation job
3. Check the logs - you should see:
   ```
   Translation input - Source: https://doctranslationstoragecbo.blob.core.windows.net/...
   Batch translation started with operation ID: {guid}
   Initial operation status: NotStarted (or Running)
   ```

If you still see `ValidationFailed`:
- Wait longer (permissions can take up to 10 minutes)
- Verify the role was actually assigned (Step 4)
- Check storage account firewall settings (see Troubleshooting below)

---

## Solution 2: Use SAS Tokens (Quick Fix)

If you can't wait for managed identity or need a quick fix, use SAS tokens.

### Add SAS Token Generation Method

Add this interface method to `IBlobStorageService`:

```csharp
public interface IBlobStorageService
{
    // ...existing methods...
    
    /// <summary>
    /// Generate a SAS token URI for a folder path (for Translation Service access)
    /// </summary>
    Uri GenerateFolderSasUri(string folderPath, TimeSpan expirationTime);
}
```

Add this implementation to `BlobStorageService.cs`:

```csharp
using Azure.Storage.Sas;
using Azure.Storage;

public class BlobStorageService : IBlobStorageService
{
    // ...existing code...
    
    public Uri GenerateFolderSasUri(string folderPath, TimeSpan expirationTime)
    {
        try
        {
            _logger.LogInformation("Generating SAS URI for folder {FolderPath} with expiration {Expiration}", 
                folderPath, expirationTime);

            // Build SAS permissions for Translation Service
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _settings.ContainerName,
                Resource = "c", // Container-level SAS
                StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5), // Allow for clock skew
                ExpiresOn = DateTimeOffset.UtcNow.Add(expirationTime),
                Protocol = SasProtocol.Https
            };

            // Translation Service needs Read and List permissions
            sasBuilder.SetPermissions(
                BlobContainerSasPermissions.Read | 
                BlobContainerSasPermissions.List |
                BlobContainerSasPermissions.Write // For target folder
            );

            // Generate the SAS token using the service client
            var sasUri = _containerClient.GenerateSasUri(sasBuilder);
            
            // Append the folder path to the SAS URI
            var uriBuilder = new UriBuilder(sasUri)
            {
                Path = $"{_containerClient.Uri.AbsolutePath}/{folderPath}".TrimStart('/')
            };

            _logger.LogInformation("SAS URI generated successfully for {FolderPath}", folderPath);
            
            return uriBuilder.Uri;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating SAS URI for folder {FolderPath}", folderPath);
            throw;
        }
    }
}
```

### Update DocumentTranslationService to Use SAS Tokens

Modify `StartBatchTranslationAsync` to use SAS tokens:

```csharp
private async Task<string> StartBatchTranslationAsync(string sourceFolderPath, string targetFolderPath, string? sourceLanguage,
    List<string> targetLanguages, bool autoDetect, CancellationToken cancellationToken)
{
    try
    {
        var inputs = new List<DocumentTranslationInput>();

        foreach (var targetLang in targetLanguages)
        {
            var targetFolder = $"{targetFolderPath}/{targetLang}";
            
            await _blobStorageService.EnsureFolderExistsAsync(targetFolder, cancellationToken);
            _logger.LogInformation("Target folder ensured: {TargetFolder}", targetFolder);
            
            // Option 1: Try managed identity first (current approach)
            // Option 2: Use SAS tokens (uncomment below)
            
            // === UNCOMMENT THIS TO USE SAS TOKENS ===
            /*
            // Generate SAS URIs with 48-hour expiration
            var sasExpiration = TimeSpan.FromHours(48);
            var sourceUri = _blobStorageService.GenerateFolderSasUri(sourceFolderPath, sasExpiration);
            var targetUri = _blobStorageService.GenerateFolderSasUri(targetFolder, sasExpiration);
            */
            
            // === COMMENT THIS OUT IF USING SAS TOKENS ===
            // Get container URIs from BlobContainerClient (managed identity)
            var blobUri = new Uri($"https://{_blobSettings.AccountName}.blob.core.windows.net");
            var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(blobUri, _credentialService.GetBlobStorageCredential());
            var containerClient = blobServiceClient.GetBlobContainerClient(_blobSettings.ContainerName);
            
            var containerUriString = containerClient.Uri.ToString().TrimEnd('/');
            var sourceUri = new Uri($"{containerUriString}/{sourceFolderPath}");
            var targetUri = new Uri($"{containerUriString}/{targetFolder}");
            // === END MANAGED IDENTITY SECTION ===

            _logger.LogInformation("Translation input - Source: {SourceUri}, Target: {TargetUri}, Language: {TargetLang}", 
                sourceUri, targetUri, targetLang);

            inputs.Add(new DocumentTranslationInput(sourceUri, targetUri, targetLang));
        }

        // ...rest of the method...
    }
}
```

### Test SAS Token Approach

1. Uncomment the SAS token code section
2. Comment out the managed identity section
3. Restart your application
4. Submit a translation job
5. Check that URIs now have `?sv=...&sig=...` query parameters in logs

---

## Troubleshooting

### Still Getting InvalidDocumentAccessLevel?

#### 1. Check Managed Identity Is Actually Enabled
```powershell
az cognitiveservices account identity show `
    --name YOUR_TRANSLATION_SERVICE `
    --resource-group YOUR_RESOURCE_GROUP
```

Should show:
```json
{
  "principalId": "abc-123-def-456...",
  "tenantId": "xyz-789...",
  "type": "SystemAssigned"
}
```

If `principalId` is null or missing, managed identity is not enabled.

#### 2. Verify Role Assignment Exists
```powershell
az role assignment list `
    --assignee YOUR_PRINCIPAL_ID `
    --all `
    --output table
```

Look for:
- ? Role: **Storage Blob Data Contributor**
- ? Scope: Includes your storage account

#### 3. Check Storage Account Firewall

If your storage account has firewall rules enabled:

1. Go to **Storage Account** ? **Networking**
2. Check **Firewall and virtual networks** settings
3. Either:
   - **Option A**: Add Translation Service's network to allowed list
   - **Option B**: Enable **"Allow Azure services on the trusted services list"**
   - **Option C**: Set to **"Enabled from all networks"** (least secure, for testing only)

#### 4. Verify Container Exists

```powershell
az storage container show `
    --name doctranslation `
    --account-name doctranslationstoragecbo `
    --auth-mode login
```

Should return container details. If error, create it:

```powershell
az storage container create `
    --name doctranslation `
    --account-name doctranslationstoragecbo `
    --auth-mode login
```

#### 5. Check If Files Were Uploaded

```powershell
az storage blob list `
    --container-name doctranslation `
    --account-name doctranslationstoragecbo `
    --prefix "jobs/" `
    --auth-mode login `
    --output table
```

Should show your uploaded files in the job folder.

#### 6. Test Blob Access Directly

Try accessing a blob URL directly in your browser:
```
https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/YOUR_JOB_ID/source/YOUR_FILE.pdf
```

- If you get **403 Forbidden**: Permission issue
- If you get **404 Not Found**: File doesn't exist
- If you get **"ResourceNotFound"** XML: Container doesn't exist

---

## Comparison: Managed Identity vs SAS Tokens

| Aspect | Managed Identity | SAS Tokens |
|--------|------------------|------------|
| **Security** | ? Most secure | ?? Tokens exposed in URLs |
| **Setup Time** | ?? 5-10 min wait for propagation | ? Immediate |
| **Configuration** | ?? Azure Portal required | ?? Code only |
| **Token Expiration** | ? Never expires | ? Expires after set time |
| **Credential Rotation** | ? Automatic | ?? Manual regeneration |
| **Audit Logging** | ? Full Azure IAM audit trail | ?? Limited |
| **Best For** | Production environments | Testing / Quick fixes |

---

## Recommended Approach

1. **Start with Managed Identity** (Solution 1)
   - More secure and sustainable
   - Required for production
   - Takes 5-10 minutes to set up

2. **Use SAS Tokens for Testing** (Solution 2)
   - While waiting for managed identity to propagate
   - To quickly verify your code logic works
   - To rule out other issues

3. **Switch Back to Managed Identity**
   - Once permissions have propagated
   - Before deploying to production
   - For long-term stability

---

## Quick Reference Commands

### Check Translation Service Identity:
```powershell
az cognitiveservices account identity show --name YOUR_NAME --resource-group YOUR_RG
```

### Check Role Assignments:
```powershell
az role assignment list --assignee YOUR_PRINCIPAL_ID --scope /subscriptions/SUB/resourceGroups/RG/providers/Microsoft.Storage/storageAccounts/STORAGE
```

### Grant Storage Access:
```powershell
az role assignment create --role "Storage Blob Data Contributor" --assignee PRINCIPAL_ID --scope "/subscriptions/SUB/resourceGroups/RG/providers/Microsoft.Storage/storageAccounts/STORAGE"
```

### List Storage Blobs:
```powershell
az storage blob list --container-name doctranslation --account-name doctranslationstoragecbo --prefix "jobs/" --auth-mode login
```

---

## Success Indicators

You'll know it's working when you see:

### In Application Logs:
```
? Translation input - Source: https://doctranslationstoragecbo.blob.core.windows.net/...
? Batch translation started with operation ID: abc-123-def-456
? Initial operation status: NotStarted (or Running, not ValidationFailed)
```

### In Azure Portal:
1. Go to **Translation Service** ? **Document Translation**
2. You should see your job with status **"Running"** or **"Succeeded"**
3. NOT **"ValidationFailed"**

### In Status Check:
```json
{
  "jobId": "abc-123",
  "status": "Succeeded",
  "totalDocuments": 1,
  "translatedDocuments": 1,
  "failedDocuments": 0
}
```

---

## Still Having Issues?

If you've tried both solutions and still getting `InvalidDocumentAccessLevel`:

1. **Check Application Insights / Logs** for exact error details
2. **Verify all URIs** in logs are correctly formatted
3. **Test with a simple single-file translation** first
4. **Check Azure Service Health** - there might be an outage
5. **Contact Azure Support** with:
   - Your Translation Service resource ID
   - Your Storage Account resource ID
   - Job ID that failed
   - Exact error message from logs

---

## Summary

The **`InvalidDocumentAccessLevel`** error means Azure Translation Service cannot access your blob storage.

**Fix it by:**
1. ? **Enabling managed identity** on Translation Service
2. ? **Granting "Storage Blob Data Contributor" role** to that identity
3. ?? **Waiting 5-10 minutes** for permissions to propagate

**Or use SAS tokens** as a quick alternative (less secure, requires code changes).

Your current code **already supports managed identity** - you just need to configure it in Azure Portal.
