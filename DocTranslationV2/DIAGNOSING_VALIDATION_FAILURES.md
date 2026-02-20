# Diagnosing Validation Failures - Step by Step

## What to Check Now

With the enhanced logging, run a translation job and check your logs (Console or Application Insights) for these specific messages:

### 1. Check the Logs for These Key Messages

Look for these log entries in order:

```
Starting batch translation with {InputCount} input(s)
Translation input - Source: {SourceUri}, Target: {TargetUri}, Language: {TargetLang}
Batch translation started with operation ID: {OperationId}
Initial operation status: {Status}
```

**What to look for:**
- Is the `SourceUri` correct? Should be: `https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/{jobId}/source`
- Is the `TargetUri` correct? Should be: `https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/{jobId}/target/{language}`
- What is the initial status? If it says "ValidationFailed" immediately, read the document errors below it

### 2. Check for Immediate Document Errors

If validation fails immediately, you should see:
```
Job {JobId} failed immediately with status: ValidationFailed
Document error at creation: Document: {URI}, Error: {Code} - {Message}
```

**Common Error Codes and What They Mean:**

| Error Code | Meaning | Solution |
|------------|---------|----------|
| `Unauthorized` | Translation Service can't access blob storage | Check managed identity permissions |
| `InvalidDocumentAccessLevel` | Blob access level is wrong | Blobs need to be accessible to the service |
| `InvalidRequest` | URI format is wrong | Check that URIs are correctly formatted |
| `ResourceNotFound` | Container or folder doesn't exist | Verify container name and that folder was created |
| `AuthenticationFailed` | Managed identity not configured | Enable managed identity on Translation Service |

### 3. Verify Azure Configuration

#### A. Check Translation Service Managed Identity

1. Go to **Azure Portal** ? Your **Translation Service** (`translationcbo`)
2. Click **Identity** in the left menu
3. Under **System assigned**, verify:
   - ? Status is **On**
   - ? You see an **Object (principal) ID** (this is the managed identity)

Copy the Object ID, you'll need it next.

#### B. Check Storage Account Permissions

1. Go to **Azure Portal** ? Your **Storage Account** (`doctranslationstoragecbo`)
2. Click **Access Control (IAM)** in the left menu
3. Click **Role assignments** tab
4. Look for the Translation Service's managed identity (search by the Object ID you copied)

**You should see:**
- ? Role: **Storage Blob Data Contributor**
- ? Assigned to: Your Translation Service's managed identity
- ? Scope: This storage account

**If you DON'T see this:**

```bash
# Add the role assignment (replace with your values)
az role assignment create \
  --role "Storage Blob Data Contributor" \
  --assignee <TRANSLATION-SERVICE-OBJECT-ID> \
  --scope /subscriptions/<SUBSCRIPTION-ID>/resourceGroups/<RESOURCE-GROUP>/providers/Microsoft.Storage/storageAccounts/doctranslationstoragecbo
```

**?? Important:** After adding the role, wait 5-10 minutes for it to propagate through Azure's system.

#### C. Check Storage Account Firewall

1. In Storage Account, click **Networking** in the left menu
2. Under **Firewalls and virtual networks**, check the setting:

**Option 1 (Recommended for Testing):**
- ? Select **"Enabled from all networks"** temporarily to rule out firewall issues

**Option 2 (For Production):**
- Select **"Enabled from selected virtual networks and IP addresses"**
- ? Check **"Allow Azure services on the trusted services list to access this storage account"**
- This allows Azure Translation Service to access even with firewall enabled

#### D. Verify Container Exists

1. In Storage Account, click **Containers** in the left menu
2. Verify you see a container named: **`doctranslation`** (or whatever your `ContainerName` setting is)
3. Click on the container
4. You should see a `jobs` folder with your recent job folders inside

### 4. Test Blob Access Manually

To verify the Translation Service can access your blobs, check the actual permissions:

1. Go to **Storage Account** ? **Containers** ? **doctranslation**
2. Click **Access Control (IAM)**
3. Click **Check access** (at the top)
4. Search for your Translation Service by name or Object ID
5. Click on it
6. You should see **Storage Blob Data Contributor** listed

If not listed here, the role assignment is at the storage account level, which is also fine.

### 5. Common Scenarios and Solutions

#### Scenario A: "Unauthorized" Error
```
Document error: Document: /jobs/xxx/source/file.pdf, Error: Unauthorized - Access denied
```

**Problem:** Translation Service doesn't have permission  
**Solution:**
1. Verify managed identity is enabled (step 3A)
2. Add Storage Blob Data Contributor role (step 3B)
3. Wait 10 minutes
4. Try again

#### Scenario B: "ResourceNotFound" Error
```
Document error: Document: /jobs/xxx/source/file.pdf, Error: ResourceNotFound - The specified resource does not exist
```

**Problem:** Container or folder doesn't exist  
**Solution:**
1. Check container name in `appsettings.Development.json` matches actual container
2. Verify the container exists in Storage Account
3. Check that files were actually uploaded to the source folder

#### Scenario C: "InvalidRequest" Error
```
Document error: Error: InvalidRequest - The request URI is invalid
```

**Problem:** URI format is wrong  
**Solution:**
1. Check `AccountName` in appsettings matches storage account name
2. Check `ContainerName` in appsettings matches actual container
3. Verify no typos in configuration

#### Scenario D: Job Succeeds but No Files Translated
```
Status: Succeeded
Total: 0, Succeeded: 0, Failed: 0
```

**Problem:** Files weren't uploaded to source folder  
**Solution:**
1. Check logs for "UploadFileAsync" messages
2. Verify files are in Storage Account ? Container ? jobs/{jobId}/source
3. Check file formats are supported

### 6. Quick Verification Checklist

Run through this checklist:

- [ ] Translation Service has managed identity enabled
- [ ] Managed identity has "Storage Blob Data Contributor" role on storage account
- [ ] Storage Account firewall allows Azure services OR is disabled
- [ ] Container "doctranslation" exists
- [ ] Files are visible in jobs/{jobId}/source folder
- [ ] appsettings.Development.json has correct AccountName and ContainerName
- [ ] Waited at least 10 minutes after adding role assignment

### 7. Testing Configuration

Here's a quick PowerShell script to verify your configuration:

```powershell
# Set your values
$translationServiceName = "translationcbo"
$storageAccountName = "doctranslationstoragecbo"
$resourceGroup = "YOUR_RESOURCE_GROUP"

# Get Translation Service managed identity
Write-Host "Checking Translation Service managed identity..."
$identity = az cognitiveservices account identity show `
    --name $translationServiceName `
    --resource-group $resourceGroup `
    --query principalId -o tsv

if ($identity) {
    Write-Host "? Managed identity found: $identity"
} else {
    Write-Host "? Managed identity NOT found - needs to be enabled"
    exit
}

# Check role assignment
Write-Host "`nChecking Storage Blob Data Contributor role..."
$roleAssignment = az role assignment list `
    --assignee $identity `
    --query "[?roleDefinitionName=='Storage Blob Data Contributor']" -o json | ConvertFrom-Json

if ($roleAssignment) {
    Write-Host "? Storage Blob Data Contributor role found"
    Write-Host "   Scope: $($roleAssignment[0].scope)"
} else {
    Write-Host "? Storage Blob Data Contributor role NOT found"
    Write-Host "   Run this command to add it:"
    Write-Host "   az role assignment create --role 'Storage Blob Data Contributor' --assignee $identity --scope /subscriptions/YOUR_SUB/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccountName"
}

# Check storage account exists
Write-Host "`nChecking storage account..."
$storageAccount = az storage account show `
    --name $storageAccountName `
    --resource-group $resourceGroup `
    --query name -o tsv

if ($storageAccount) {
    Write-Host "? Storage account found: $storageAccount"
} else {
    Write-Host "? Storage account NOT found"
}
```

### 8. What the Enhanced Logging Will Show

After making the code changes, when you run a translation job, you'll see:

**If everything is working:**
```
Starting batch translation with 1 input(s)
Translation input - Source: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc-123/source
Translation input - Target: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc-123/target/es
Batch translation started with operation ID: abc-123
Initial operation status: Running
Document counts - Total: 1, Failed: 0, NotStarted: 0
```

**If validation fails:**
```
Starting batch translation with 1 input(s)
Translation input - Source: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc-123/source
Translation input - Target: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc-123/target/es
Batch translation started with operation ID: abc-123
Initial operation status: ValidationFailed
Job abc-123 failed immediately with status: ValidationFailed
Document error at creation: Document: https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc-123/source/test.pdf, Error: Unauthorized - The Translation Service does not have permission to access the storage account
```

This will tell you EXACTLY why validation is failing!

### 9. Next Steps

1. **Deploy the code changes** with enhanced logging
2. **Run a translation job**
3. **Check the logs** for the detailed error messages
4. **Share the exact error code and message** if you need help interpreting it

The logs will now show you the real reason for the validation failure, not just generic guidance.
