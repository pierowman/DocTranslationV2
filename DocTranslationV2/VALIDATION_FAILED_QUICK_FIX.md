# ValidationFailed Quick Fix

## The Problem
Jobs showing "ValidationFailed" and logs keep repeating the same status check.

## Root Cause
Azure Translation Service **cannot access your blob storage** because the Translation Service's managed identity doesn't have the required permissions.

## Quick Fix (5 minutes + 10 minute wait)

### 1. Enable Managed Identity
```bash
az cognitiveservices account identity assign \
    --name YOUR_TRANSLATION_SERVICE \
    --resource-group YOUR_RESOURCE_GROUP
```

### 2. Get the Principal ID
Look for `principalId` in the output, e.g., `12345678-1234-1234-1234-123456789abc`

### 3. Grant Storage Access
```bash
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee YOUR_PRINCIPAL_ID_FROM_STEP_2 \
    --scope /subscriptions/YOUR_SUB/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE
```

### 4. Wait 10 Minutes ?
Role assignments take 5-10 minutes to propagate. Go get coffee ?

### 5. Try Again
Create a new translation job. It should now work!

## Verify It Worked
```bash
az role assignment list \
    --assignee YOUR_PRINCIPAL_ID \
    --query "[?roleDefinitionName=='Storage Blob Data Contributor']"
```

Should return a non-empty list.

## Code Changes

? **Status check now detects ValidationFailed** and returns detailed error  
? **UI shows validation failed with warning icon**  
? **Logs don't repeat endlessly** for failed jobs  
? **Error message explains how to fix** the issue  

## What You'll See Now

### Before (Logs kept repeating):
```
Translation job abc status: ValidationFailed
Checking status for translation job abc
Translation job abc status: ValidationFailed
Checking status for translation job abc
Translation job abc status: ValidationFailed
...repeats forever...
```

### After (Returns error immediately):
```
Translation job abc status: ValidationFailed
Translation job abc failed validation. Check managed identity permissions.
```

Then it stops checking that job.

## See Full Details
- `VALIDATION_FAILED_FIX.md` - Complete troubleshooting guide
- `MANAGED_IDENTITY_SETUP.md` - Step-by-step permission setup

## TL;DR
1. Run commands above
2. Wait 10 minutes
3. Try translation again
4. Should work now! ??
