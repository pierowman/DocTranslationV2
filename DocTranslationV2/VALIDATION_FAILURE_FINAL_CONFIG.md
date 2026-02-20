# Validation Failure Resolution - Final Configuration

## Current Implementation

The code now uses **folder-level URIs with Managed Identity authentication**, which is the correct approach when you have one container with multiple folders (your setup).

### URIs Being Used
```
Source: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/{jobId}/source
Target: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/{jobId}/target/{language}
```

### Authentication Method
- **Managed Identity** (not SAS tokens)
- Translation Service uses its system-assigned identity
- Identity must have **"Storage Blob Data Contributor"** role

## Difference from Old Version

Your old version uses:
- **Separate containers** for source and target
- **Container-level URIs** (pointing to entire containers)

Your new version uses:
- **Single container** with folder paths
- **Folder-level URIs** (pointing to specific folders within container)

**Both approaches work with Managed Identity**, but folder-level URIs are more flexible for organizing multiple jobs.

## Required Azure Configuration

### 1. Enable Translation Service Managed Identity

```bash
# Check if enabled
az cognitiveservices account identity show \
  --name translationcbo \
  --resource-group <YOUR_RESOURCE_GROUP>

# If not enabled, enable it
az cognitiveservices account identity assign \
  --name translationcbo \
  --resource-group <YOUR_RESOURCE_GROUP>
```

**In Azure Portal:**
1. Go to Translation Service ? Identity
2. Under "System assigned" tab
3. Set Status to **On**
4. Copy the **Object (principal) ID** - you'll need this next

### 2. Grant Storage Access to Translation Service

The Translation Service's managed identity needs permission to read source files and write target files.

```bash
# Get the managed identity principal ID from step 1
TRANSLATION_PRINCIPAL_ID="<Object-ID-from-step-1>"

# Assign role (replace with your values)
az role assignment create \
  --role "Storage Blob Data Contributor" \
  --assignee $TRANSLATION_PRINCIPAL_ID \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/<RESOURCE_GROUP>/providers/Microsoft.Storage/storageAccounts/doctranslationstoragecbo
```

**In Azure Portal:**
1. Go to Storage Account (`doctranslationstoragecbo`) ? Access Control (IAM)
2. Click **+ Add** ? **Add role assignment**
3. Select role: **Storage Blob Data Contributor**
4. Click **Next**
5. **Assign access to**: Managed identity
6. Click **+ Select members**
7. Filter by **Cognitive Services** and select your Translation Service
8. Click **Select** ? **Review + assign**

### 3. Wait for Permission Propagation

?? **CRITICAL**: After adding the role, wait **5-10 minutes** before testing. Azure needs time to propagate the permission changes across its infrastructure.

### 4. Verify Configuration

```bash
# Verify the role assignment exists
az role assignment list \
  --assignee <TRANSLATION_PRINCIPAL_ID> \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/<RESOURCE_GROUP>/providers/Microsoft.Storage/storageAccounts/doctranslationstoragecbo \
  --query "[?roleDefinitionName=='Storage Blob Data Contributor']" \
  --output table
```

You should see output showing the role assignment.

## Testing the Configuration

### Step 1: Run a Translation Job

Upload a file and start a translation. Watch the logs for:

```
Translation input - Source: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/{jobId}/source
Translation input - Target: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/{jobId}/target/es
IMPORTANT: Translation Service must have 'Storage Blob Data Contributor' role on storage account 'doctranslationstoragecbo'
Starting batch translation with 1 input(s)
Batch translation started with operation ID: {operationId}
```

### Step 2: Check Initial Status

Within 5 seconds, check the logs:

**If working correctly:**
```
Initial operation status: Running
Initial document counts - Total: 1, Failed: 0, NotStarted: 0
```

**If validation failed:**
```
Initial operation status: ValidationFailed
Job {JobId} failed immediately with status: ValidationFailed
Document error at creation: Document: {URI}, Error: Unauthorized - The Translation Service does not have permission to access the storage account
```

### Step 3: After 5 Seconds

The code automatically checks again:

**If working:**
```
After 5 second delay - Status: Running, HasValue: True
Document counts after delay - Total: 1, Succeeded: 0, Failed: 0, NotStarted: 0
```

**If still failing:**
```
After 5 second delay - Status: ValidationFailed, HasValue: True
VALIDATION FAILED - Check these items:
1. Translation Service '{endpoint}' has system-assigned managed identity ENABLED
2. Managed identity has 'Storage Blob Data Contributor' role on storage account 'doctranslationstoragecbo'
3. Role assignment has propagated (can take 5-10 minutes after assignment)
4. Source folder 'jobs/{jobId}/source' exists and contains files
5. Container 'doctranslation' exists in storage account
```

## Common Issues and Solutions

### Issue 1: "Unauthorized" Error

**Error:**
```
Error Code: Unauthorized
Message: The Translation Service does not have permission to access the storage account
```

**Solution:**
- Verify managed identity is enabled on Translation Service
- Verify "Storage Blob Data Contributor" role is assigned
- Wait 10 minutes after assigning role
- Check role assignment in Azure Portal under Storage Account ? IAM

### Issue 2: "ResourceNotFound" Error

**Error:**
```
Error Code: ResourceNotFound
Message: The specified resource does not exist
```

**Solution:**
- Verify container name `doctranslation` exists
- Verify files were uploaded to source folder
- Check `appsettings.Development.json` has correct container name
- Check storage account name is correct

### Issue 3: Files Upload But Job Shows 0 Documents

**Symptom:**
```
Total: 0, Failed: 0, NotStarted: 0
```

**Solution:**
- Files might be in wrong location
- Check Storage Explorer: Container ? jobs ? {jobId} ? source ? files should be here
- Verify folder path in logs matches where files were uploaded

### Issue 4: Still Failing After Waiting 10 Minutes

**Troubleshooting:**

1. **Check in Azure Portal manually:**
   - Go to Translation Service ? Document Translation
   - Look for your job in the list
   - Click on it to see Azure's error message

2. **Verify network connectivity:**
   - Go to Storage Account ? Networking
   - Temporarily set to "Enabled from all networks"
   - Try again
   - If this fixes it, the firewall was blocking

3. **Check Application Insights:**
   - Look for the detailed error logs
   - Note the exact error code
   - Search Azure documentation for that specific code

## Firewall Considerations

If your storage account has network restrictions:

### Option 1: Allow All Azure Services (Easiest)
1. Storage Account ? Networking
2. Under "Firewall and virtual networks"
3. Check: ? **"Allow Azure services on the trusted services list to access this storage account"**

### Option 2: Add Translation Service to Allowed List
1. Get Translation Service's outbound IP addresses (check Azure Portal)
2. Add these IPs to storage account firewall rules
3. Wait 10 minutes for propagation

## Expected Behavior After Correct Configuration

### Logs Will Show:
```
? Translation input - Source: https://...
? IMPORTANT: Translation Service must have 'Storage Blob Data Contributor' role
? Starting batch translation with 1 input(s)
? Batch translation started with operation ID: abc-123
? Initial operation status: Running
? After 5 second delay - Status: Running
```

### Job Will:
- ? Start with status "Running"
- ? Process documents (Total > 0)
- ? Complete successfully (Status: Succeeded)
- ? Create translated files in target folder

## Monitoring

### Application Insights Query

```kusto
traces
| where message contains "Translation input"
   or message contains "VALIDATION FAILED"
   or message contains "Document error at creation"
| project timestamp, message, severityLevel
| order by timestamp desc
```

### Key Metrics to Watch
- Time from job creation to "Running" status (should be < 10 seconds)
- Number of "ValidationFailed" jobs (should be 0 after configuration)
- Number of "Succeeded" jobs (should increase over time)

## Summary

? **Code is correct** - Uses folder-level URIs with Managed Identity  
?? **Configuration required** - Translation Service needs role assignment  
?? **Wait time** - 5-10 minutes after assigning role  
?? **Verification** - Check logs for specific error codes  

The validation failure is almost certainly a **permission issue**, not a code issue. Once the role is properly assigned and propagated, translations should work.

## Next Steps

1. ? **Enable managed identity** on Translation Service
2. ? **Assign role** to storage account
3. ?? **Wait 10 minutes**
4. ? **Test translation**
5. ?? **Check logs** for detailed errors
6. ?? **Report back** with exact error code if still failing

With proper configuration, this approach is more robust and secure than your old version, as it:
- Uses managed identity (no keys to rotate)
- Organizes jobs in folders (better structure)
- Provides detailed error logging (easier troubleshooting)
