# Validation Failed Error - Complete Guide

## What is "ValidationFailed"?

When a translation job shows **"ValidationFailed"** status, it means Azure Translation Service **cannot access your blob storage**. The job immediately fails without processing any documents.

## Why It Happens

Azure Translation Service needs to:
1. **Read files** from the source blob storage folder
2. **Write translated files** to the target blob storage folder

If the Translation Service's managed identity doesn't have permission to access blob storage, validation fails.

## The Log Pattern You're Seeing

```
Translation job a5fae7c6-cefa-4fb7-9a9c-b3ffa3b1396e status: ValidationFailed, Total: 0, Succeeded: 0, Failed: 0
Checking status for translation job a5fae7c6-cefa-4fb7-9a9c-b3ffa3b1396e
Translation job a5fae7c6-cefa-4fb7-9a9c-b3ffa3b1396e status: ValidationFailed, Total: 0, Succeeded: 0, Failed: 0
```

**Problem**: The app keeps checking status even though the job has failed permanently. ValidationFailed is a **terminal state** - the job will never progress.

## Fix #1: Stop Checking Failed Jobs

I've updated the code to:
- **Detect ValidationFailed** status
- **Return detailed error message** explaining the issue
- **Stop polling** (JavaScript doesn't keep checking)

The error message now says:
```
Validation failed: Azure Translation Service cannot access the blob storage.
This usually means:
1. The Translation Service's managed identity doesn't have 'Storage Blob Data Contributor' role
2. Role assignment hasn't propagated yet (wait 5-10 minutes)
3. Blob storage URIs are incorrect

See MANAGED_IDENTITY_SETUP.md for instructions.
```

## Fix #2: Set Up Permissions

The most common cause is **missing permissions**. Here's how to fix it:

### Step 1: Enable Managed Identity on Translation Service

```bash
az cognitiveservices account identity assign \
    --name YOUR_TRANSLATION_SERVICE_NAME \
    --resource-group YOUR_RESOURCE_GROUP
```

**Example**:
```bash
az cognitiveservices account identity assign \
    --name translationcbo \
    --resource-group DocTranslation-RG
```

**Output**:
```json
{
  "principalId": "12345678-1234-1234-1234-123456789abc",
  "tenantId": "abcdefgh-abcd-abcd-abcd-abcdefghijkl",
  "type": "SystemAssigned"
}
```

**SAVE THE `principalId`** - you need it for the next step!

### Step 2: Grant Storage Access

```bash
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee YOUR_TRANSLATION_PRINCIPAL_ID \
    --scope /subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/YOUR_RESOURCE_GROUP/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE_ACCOUNT
```

**Example**:
```bash
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee 12345678-1234-1234-1234-123456789abc \
    --scope /subscriptions/abc-def-ghi/resourceGroups/DocTranslation-RG/providers/Microsoft.Storage/storageAccounts/doctranslationstorage
```

### Step 3: Verify Permission

```bash
az role assignment list \
    --assignee YOUR_TRANSLATION_PRINCIPAL_ID \
    --query "[?roleDefinitionName=='Storage Blob Data Contributor']" \
    --output table
```

**Expected output** (should not be empty):
```
Principal                            Role                           Scope
-----------------------------------  -----------------------------  -------------
12345678-1234-1234-1234-123456789abc Storage Blob Data Contributor /subscriptions/.../doctranslationstorage
```

### Step 4: Wait for Propagation

**IMPORTANT**: Role assignments can take **5-10 minutes** to propagate through Azure AD.

- ?? **Wait 10 minutes** after creating the role assignment
- ? Go get coffee
- ? Then try your translation again

## Fix #3: Verify Blob URIs

Check the application logs for lines like:
```
Translation input - Source: https://ACCOUNT.blob.core.windows.net/CONTAINER/jobs/JOBID/source
Translation input - Target: https://ACCOUNT.blob.core.windows.net/CONTAINER/jobs/JOBID/target/es
```

Verify:
1. **Account name** matches your storage account
2. **Container name** is correct (usually "translations")
3. **Files exist** in the source folder
4. **URIs are accessible**

Test manually:
```bash
# List files in source folder (should show your uploaded files)
az storage blob list \
    --container-name translations \
    --prefix "jobs/JOBID/source/" \
    --account-name YOUR_STORAGE \
    --auth-mode login
```

## What Changed in the Code

### DocumentTranslationService.cs - GetTranslationStatusAsync

Added detection for ValidationFailed:

```csharp
// Handle ValidationFailed status with detailed error information
if (statusString == "ValidationFailed")
{
    _logger.LogError("Translation job {JobId} failed validation...", jobId);
    
    jobStatus.ErrorMessage = "Validation failed: Azure Translation Service cannot access the blob storage. " +
        "This usually means:\n" +
        "1. The Translation Service's managed identity doesn't have 'Storage Blob Data Contributor' role\n" +
        "2. Role assignment hasn't propagated yet (wait 5-10 minutes)\n" +
        "3. Blob storage URIs are incorrect\n\n" +
        $"Job ID: {jobId}\n" +
        "See MANAGED_IDENTITY_SETUP.md for instructions.";
    
    return jobStatus; // Return immediately, don't keep checking
}
```

### Jobs.cshtml - JavaScript

Added:
- **ValidationFailed** to status filter dropdown
- **ValidationFailed** counter in summary
- **Warning icon** for validation failed jobs
- **Error message truncation** with full text on hover

## Testing the Fix

1. **Create a new translation job**
2. **Wait a few seconds**
3. **Check the Jobs page**

### If Permissions Are Correct:
- Status shows "Running"
- Progress bar animates
- Eventually shows "Succeeded"

### If Permissions Are Missing:
- Status shows "ValidationFailed" with warning icon
- Error column shows truncated message
- Hover over error to see full message
- **Status doesn't keep repeating** in logs

## Diagnostic Checklist

- [ ] Translation Service has system-assigned managed identity enabled
- [ ] Translation Service managed identity has "Storage Blob Data Contributor" role on storage account
- [ ] Role assignment has propagated (waited 5-10 minutes)
- [ ] Blob URIs in logs are correct
- [ ] Files were successfully uploaded to source folder
- [ ] Web app managed identity also has "Storage Blob Data Contributor" (for uploads)

## Quick Test Command

Run this to verify everything is set up:

```bash
#!/bin/bash

# Variables - REPLACE THESE
TRANSLATION_SERVICE="translationcbo"
RESOURCE_GROUP="DocTranslation-RG"
STORAGE_ACCOUNT="doctranslationstorage"

echo "Checking Translation Service managed identity..."
PRINCIPAL_ID=$(az cognitiveservices account identity show \
    --name $TRANSLATION_SERVICE \
    --resource-group $RESOURCE_GROUP \
    --query principalId -o tsv)

if [ -z "$PRINCIPAL_ID" ]; then
    echo "? ERROR: Managed identity not enabled on Translation Service"
    echo "Run: az cognitiveservices account identity assign --name $TRANSLATION_SERVICE --resource-group $RESOURCE_GROUP"
    exit 1
fi

echo "? Managed identity enabled: $PRINCIPAL_ID"

echo ""
echo "Checking storage permissions..."
ROLE=$(az role assignment list \
    --assignee $PRINCIPAL_ID \
    --query "[?roleDefinitionName=='Storage Blob Data Contributor'].roleDefinitionName" \
    --output tsv)

if [ -z "$ROLE" ]; then
    echo "? ERROR: Translation Service doesn't have Storage Blob Data Contributor role"
    echo "Run:"
    echo "az role assignment create \\"
    echo "  --role 'Storage Blob Data Contributor' \\"
    echo "  --assignee $PRINCIPAL_ID \\"
    echo "  --scope /subscriptions/YOUR_SUB/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT"
    exit 1
fi

echo "? Storage Blob Data Contributor role assigned"
echo ""
echo "?? All checks passed! Your Translation Service should be able to access blob storage."
echo "??  If you just added the role, wait 5-10 minutes for it to propagate."
```

Save as `check-permissions.sh` and run:
```bash
chmod +x check-permissions.sh
./check-permissions.sh
```

## Common Errors

### "Object reference not set"
If you still see this after ValidationFailed, it's the SDK bug. The validation failed detection should prevent this now.

### "403 Forbidden"
This confirms it's a permissions issue. Follow steps above.

### "404 Not Found"
The blob container or files don't exist. Check your uploads.

## Expected Behavior After Fix

### In Logs:
```
Translation job abc-123 status: ValidationFailed, Total: 0, Succeeded: 0, Failed: 0
Translation job abc-123 failed validation. This typically means the Translation Service cannot access blob storage.
Validation failed for job abc-123. Check that Translation Service managed identity has Storage Blob Data Contributor role on the storage account.
```

**Then it stops checking** - no more repeated log entries!

### In UI:
- Job shows with **red "Validation Failed"** badge
- Error column shows truncated error message
- Hovering shows full error with instructions
- **Auto-refresh continues** but doesn't spam the server

## Still Having Issues?

If permissions are set correctly but validation still fails:

1. **Check Azure Portal** ? Translation Service ? Document Translation
   - Does the job appear there?
   - What does Azure say is the error?

2. **Check blob storage**:
   ```bash
   az storage blob list --container-name translations --auth-mode login --account-name YOUR_STORAGE
   ```
   - Are your files there?
   - In the correct folder structure?

3. **Check network**:
   - Is your storage account behind a firewall?
   - Does it allow access from Azure services?

4. **Try REST API** to see raw error:
   ```bash
   az rest --method get \
       --url "https://YOUR_TRANSLATION.cognitiveservices.azure.com/translator/document/batches/JOB_ID?api-version=2024-05-01"
   ```

## Summary

? **Code now detects ValidationFailed** and returns helpful error  
? **UI shows validation failed status** prominently  
? **Logs don't spam** with repeated checks  
? **Error message explains** how to fix the issue  
? **Instructions provided** for setting up permissions  

The job will **fail immediately** with ValidationFailed if permissions are wrong, and the system will **stop checking** that job's status repeatedly.

**Next step**: Follow the permission setup steps above and wait 10 minutes before trying again! ?
