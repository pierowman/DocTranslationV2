# NullReferenceException Troubleshooting Guide

## Issue Overview

Getting `NullReferenceException` when calling `GetTranslationStatusAsync` or during translation operations.

---

## ? Recent Fixes Applied

### 1. **Fixed Blob Storage URIs**
- **Was**: Pointing to translator API endpoint
- **Now**: Pointing to actual blob storage containers

### 2. **Added Retry Logic**
- Exponential backoff (1s, 2s, 4s)
- Handles operations that aren't ready yet
- Graceful handling of 404 and NullReference errors

### 3. **Added Initialization Delay**
- 2-second delay after starting translation
- Gives Azure time to initialize the operation

---

## ?? Root Causes & Solutions

### Cause 1: Azure Permissions Not Set
**Symptom**: Translation starts but status check fails

**Solution**: Verify permissions are set correctly

```bash
# Check if your App Registration has Storage Blob Data Contributor role
az role assignment list \
    --assignee YOUR_CLIENT_ID \
    --scope /subscriptions/YOUR_SUBSCRIPTION/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE

# Check if Translation Service has managed identity enabled
az cognitiveservices account identity show \
    --name YOUR_TRANSLATION_SERVICE \
    --resource-group YOUR_RG

# Check if Translation Service managed identity has Storage access
az role assignment list \
    --assignee MANAGED_IDENTITY_PRINCIPAL_ID \
    --scope /subscriptions/YOUR_SUBSCRIPTION/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE
```

### Cause 2: Role Assignment Not Propagated
**Symptom**: Works after waiting 5-10 minutes

**Solution**: Wait for Azure AD propagation (can take up to 5 minutes)

```bash
# Force a re-check after role assignment
az account get-access-token --resource https://storage.azure.com/

# Wait 5 minutes after assigning roles before testing
```

### Cause 3: Blob Container Doesn't Exist
**Symptom**: Translation fails to start or immediate 404

**Solution**: Verify container exists

```bash
# Check if container exists
az storage container show \
    --name translations \
    --account-name YOUR_STORAGE \
    --auth-mode login

# Create if missing
az storage container create \
    --name translations \
    --account-name YOUR_STORAGE \
    --auth-mode login
```

### Cause 4: Files Not Actually Uploaded
**Symptom**: Translation starts but finds no documents

**Solution**: Verify files are in blob storage

```bash
# List files in source folder
az storage blob list \
    --container-name translations \
    --prefix jobs/YOUR_JOB_ID/source \
    --account-name YOUR_STORAGE \
    --auth-mode login

# Check file sizes
az storage blob list \
    --container-name translations \
    --prefix jobs/YOUR_JOB_ID/source \
    --account-name YOUR_STORAGE \
    --auth-mode login \
    --query "[].{name:name, size:properties.contentLength}"
```

### Cause 5: Translation Service Not Ready
**Symptom**: NullReferenceException immediately after starting translation

**Solution**: Already fixed with retry logic, but you can also:

1. **Check Translation Service Status**
```bash
az cognitiveservices account show \
    --name YOUR_TRANSLATION_SERVICE \
    --resource-group YOUR_RG \
    --query "{name:name, provisioningState:properties.provisioningState, endpoint:properties.endpoint}"
```

2. **Verify Endpoint Configuration**
```json
{
  "AzureTranslation": {
    "Endpoint": "https://YOUR_SERVICE.cognitiveservices.azure.com/"
  }
}
```

### Cause 6: Incorrect Endpoint Format
**Symptom**: Translation service can't be reached

**Solution**: Verify endpoint format

? **Correct**: `https://your-translator.cognitiveservices.azure.com/`
? **Wrong**: `https://your-translator.cognitiveservices.azure.com/translator/text/batch/v1.0`
? **Wrong**: `your-translator.cognitiveservices.azure.com` (missing https)

### Cause 7: Authentication Issues
**Symptom**: Authentication errors before NullReference

**Solution**: Verify credentials

```bash
# Test authentication
az login --tenant YOUR_TENANT_ID

# Verify app can authenticate
az ad app show --id YOUR_CLIENT_ID

# Test service principal
az login --service-principal \
    --username YOUR_CLIENT_ID \
    --password YOUR_CLIENT_SECRET \
    --tenant YOUR_TENANT_ID
```

### Cause 8: Network/Firewall Issues
**Symptom**: Intermittent failures or timeouts

**Solution**: Check network settings

1. **Storage Account Firewall**
   - Azure Portal ? Storage Account ? Networking
   - Ensure your IP is allowed OR "Allow access from all networks" is enabled

2. **Translation Service Firewall**
   - Azure Portal ? Cognitive Services ? Networking
   - Ensure access is permitted

---

## ??? Debugging Steps

### Step 1: Enable Detailed Logging

Add to `appsettings.Development.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Azure": "Debug",
      "DocTranslationV2": "Debug"
    }
  }
}
```

### Step 2: Check Application Insights

1. Go to Azure Portal ? Application Insights ? YOUR_RESOURCE
2. Go to "Logs"
3. Run this query:

```kusto
traces
| where timestamp > ago(1h)
| where message contains "translation" or message contains "blob"
| project timestamp, severityLevel, message, customDimensions
| order by timestamp desc
```

### Step 3: Test Blob Storage Access

Create a test controller method:

```csharp
[HttpGet("test-blob")]
public async Task<IActionResult> TestBlobStorage()
{
    try
    {
        // Test upload
        var testData = System.Text.Encoding.UTF8.GetBytes("test");
        using var stream = new MemoryStream(testData);
        await _blobStorageService.UploadFileAsync(stream, "test.txt", "test-folder", CancellationToken.None);
        
        // Test list
        var files = await _blobStorageService.ListFilesInFolderAsync("test-folder", CancellationToken.None);
        
        // Test delete
        await _blobStorageService.DeleteFolderAsync("test-folder", CancellationToken.None);
        
        return Ok(new { success = true, filesFound = files.Count });
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
    }
}
```

### Step 4: Test Translation Service

Create another test method:

```csharp
[HttpGet("test-translation-client")]
public async Task<IActionResult> TestTranslationClient()
{
    try
    {
        var operation = await _translationService.GetTranslationStatusAsync("test-job-id");
        return Ok(new { 
            success = true, 
            status = operation.Status,
            note = "If Status='NotFound', translation client is working correctly"
        });
    }
    catch (Exception ex)
    {
        return BadRequest(new { 
            error = ex.Message, 
            innerError = ex.InnerException?.Message,
            stackTrace = ex.StackTrace 
        });
    }
}
```

### Step 5: Monitor Real Translation

When running a real translation, watch the logs:

```bash
# In Visual Studio, go to Debug ? Windows ? Output
# Or run from command line with verbose logging:
dotnet run --verbosity detailed
```

Look for these log messages:
1. ? "Starting translation job {JobId} with {FileCount} files"
2. ? "Processed {FileCount} files in parallel"
3. ? "Translation input - Source: ..., Target: ..., Language: ..."
4. ? "Batch translation started with operation ID: {OperationId}"
5. ? "Checking status for translation job {JobId}"
6. ? Any errors or warnings

---

## ?? Configuration Checklist

Use this checklist to verify your setup:

### Azure Resources
- [ ] Storage Account created
- [ ] Container `translations` exists
- [ ] Translation Service created
- [ ] App Registration created
- [ ] Client Secret generated

### Permissions
- [ ] App Registration has "Storage Blob Data Contributor" on Storage Account
- [ ] Translation Service has Managed Identity enabled
- [ ] Translation Service Managed Identity has "Storage Blob Data Contributor" on Storage Account
- [ ] Wait 5+ minutes after role assignments

### Configuration Values
- [ ] `AzureBlobStorage:AccountName` is correct
- [ ] `AzureBlobStorage:ContainerName` is "translations"
- [ ] `AzureBlobStorage:TenantId` is correct
- [ ] `AzureBlobStorage:ClientId` is correct
- [ ] `AzureBlobStorage:ClientSecret` is correct
- [ ] `AzureTranslation:Endpoint` ends with `.cognitiveservices.azure.com/`
- [ ] `AzureTranslation:Region` matches your resource location

### Network
- [ ] Storage Account allows network access
- [ ] Translation Service allows network access
- [ ] No corporate firewall blocking Azure endpoints

---

## ?? Quick Fixes

### Fix 1: Reset Permissions
```bash
# Remove old role assignments
az role assignment delete \
    --assignee YOUR_CLIENT_ID \
    --role "Storage Blob Data Contributor" \
    --scope /subscriptions/YOUR_SUBSCRIPTION/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE

# Re-add with correct scope
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee YOUR_CLIENT_ID \
    --scope /subscriptions/YOUR_SUBSCRIPTION/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE

# Wait 5 minutes
Start-Sleep -Seconds 300
```

### Fix 2: Recreate Container
```bash
# Delete and recreate container (will lose data!)
az storage container delete \
    --name translations \
    --account-name YOUR_STORAGE \
    --auth-mode login

az storage container create \
    --name translations \
    --account-name YOUR_STORAGE \
    --public-access off \
    --auth-mode login
```

### Fix 3: Restart Translation Service
```bash
# Sometimes helps with managed identity issues
az cognitiveservices account update \
    --name YOUR_TRANSLATION_SERVICE \
    --resource-group YOUR_RG \
    --tags Environment=Production
```

### Fix 4: Clear App Secrets and Re-add
```bash
# List current secrets
dotnet user-secrets list

# Clear all
dotnet user-secrets clear

# Re-add (replace with your values)
dotnet user-secrets set "AzureBlobStorage:AccountName" "YOUR_VALUE"
dotnet user-secrets set "AzureBlobStorage:TenantId" "YOUR_VALUE"
dotnet user-secrets set "AzureBlobStorage:ClientId" "YOUR_VALUE"
dotnet user-secrets set "AzureBlobStorage:ClientSecret" "YOUR_VALUE"
dotnet user-secrets set "AzureTranslation:Endpoint" "YOUR_VALUE"
dotnet user-secrets set "AzureTranslation:Region" "YOUR_VALUE"
```

---

## ?? Getting Help

If still experiencing issues:

1. **Check Application Insights** for detailed error messages
2. **Review Azure Portal** ? Storage Account ? Monitoring ? Insights
3. **Check Azure Portal** ? Translation Service ? Metrics
4. **Enable DEBUG logging** and capture full error
5. **Verify all checklist items** above

---

## ? Success Indicators

You should see these in the logs when everything works:

```
info: DocTranslationV2.Services.DocumentTranslationService[0]
      Starting translation job 12345-67890 with 1 files

info: DocTranslationV2.Services.BlobStorageService[0]
      Uploading file test.docx to blob storage at jobs/12345-67890/source/test.docx

info: DocTranslationV2.Services.BlobStorageService[0]
      Successfully uploaded file test.docx

info: DocTranslationV2.Services.DocumentTranslationService[0]
      Translation input - Source: https://ACCOUNT.blob.core.windows.net/translations/jobs/12345-67890/source, Target: https://ACCOUNT.blob.core.windows.net/translations/jobs/12345-67890/target/es, Language: es

info: DocTranslationV2.Services.DocumentTranslationService[0]
      Batch translation started with operation ID: abcd-efgh-1234

info: DocTranslationV2.Services.DocumentTranslationService[0]
      Checking status for translation job abcd-efgh-1234

info: DocTranslationV2.Services.DocumentTranslationService[0]
      Translation job abcd-efgh-1234 status: Running, Total: 1, Succeeded: 0, Failed: 0
```

---

**If retry logic keeps failing after 3 attempts, it's most likely a permissions issue. Double-check all role assignments!**
