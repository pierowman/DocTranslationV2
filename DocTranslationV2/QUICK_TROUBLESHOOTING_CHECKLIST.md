# InvalidDocumentAccessLevel - Quick Troubleshooting Checklist

## The Error
```
ErrorCode: InvalidRequest
InnerError: InvalidDocumentAccessLevel - "Cannot access source document location with the current permissions."
```

## Quick Diagnosis (Run These Commands)

### 1. Check if Managed Identity is Enabled
```powershell
# Replace with your actual values
$translationService = "YOUR_TRANSLATION_SERVICE_NAME"
$resourceGroup = "YOUR_RESOURCE_GROUP"

az cognitiveservices account identity show `
    --name $translationService `
    --resource-group $resourceGroup
```

**Expected Result:**
```json
{
  "principalId": "12345678-1234-1234-1234-123456789012",
  "tenantId": "87654321-4321-4321-4321-210987654321",
  "type": "SystemAssigned"
}
```

? **If principalId is null** ? Managed identity is NOT enabled
? **If you see a principalId** ? Managed identity IS enabled (copy this ID!)

---

### 2. Check Storage Permissions
```powershell
# Use the principalId from step 1
$principalId = "YOUR_PRINCIPAL_ID_FROM_STEP_1"
$storageAccount = "doctranslationstoragecbo"
$subscriptionId = "YOUR_SUBSCRIPTION_ID"

az role assignment list `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccount" `
    --output table
```

**Expected Result:**
```
PrincipalName              Role                           Scope
-----------------------    ----------------------------   ----
YOUR_TRANSLATION_SERVICE   Storage Blob Data Contributor  .../storageAccounts/doctranslationstoragecbo
```

? **If list is empty** ? No permissions assigned
? **If you see "Storage Blob Data Contributor"** ? Permissions ARE assigned

---

### 3. Check Storage Account Firewall
```powershell
az storage account show `
    --name $storageAccount `
    --resource-group $resourceGroup `
    --query "networkRuleSet.defaultAction" `
    --output tsv
```

**Result Interpretation:**
- **Allow** ? Open to all networks ? (Good for testing)
- **Deny** ? Firewall is active ?? (May block Translation Service)

If firewall is active, check trusted services:
```powershell
az storage account show `
    --name $storageAccount `
    --resource-group $resourceGroup `
    --query "networkRuleSet.bypass" `
    --output tsv
```

Should include: **AzureServices**

---

### 4. Verify Container Exists
```powershell
# Container name from appsettings.json: "translations"
az storage container show `
    --name translations `
    --account-name $storageAccount `
    --auth-mode login
```

? **Success** ? Container exists
? **Error "ContainerNotFound"** ? Container doesn't exist (create it!)

---

### 5. Check If Source Files Were Uploaded
```powershell
# Replace JOB_ID with your actual job ID from logs
$jobId = "YOUR_JOB_ID"

az storage blob list `
    --container-name translations `
    --account-name $storageAccount `
    --prefix "jobs/$jobId/source" `
    --auth-mode login `
    --output table
```

? **Shows files** ? Files uploaded successfully
? **Empty list** ? Files were not uploaded

---

## Decision Tree

### If Managed Identity is NOT Enabled (Step 1 failed):
```powershell
# Enable it now
az cognitiveservices account identity assign `
    --name $translationService `
    --resource-group $resourceGroup

# Get the new principal ID
$principalId = az cognitiveservices account identity show `
    --name $translationService `
    --resource-group $resourceGroup `
    --query principalId -o tsv

Write-Host "Managed Identity Principal ID: $principalId"
```
? **Then proceed to grant permissions (next step)**

---

### If Permissions are NOT Assigned (Step 2 failed):
```powershell
# Grant Storage Blob Data Contributor role
az role assignment create `
    --role "Storage Blob Data Contributor" `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccount"

Write-Host "? Role assigned!"
Write-Host "??  Wait 5-10 minutes for propagation before testing"
```

---

### If Firewall is Blocking (Step 3 shows "Deny"):

**Option A - Allow Azure Services (Recommended):**
```powershell
az storage account update `
    --name $storageAccount `
    --resource-group $resourceGroup `
    --bypass AzureServices
```

**Option B - Disable Firewall (Testing Only):**
```powershell
az storage account update `
    --name $storageAccount `
    --resource-group $resourceGroup `
    --default-action Allow
```

---

### If Container Doesn't Exist (Step 4 failed):
```powershell
# Create the container (name from appsettings.json)
az storage container create `
    --name translations `
    --account-name $storageAccount `
    --auth-mode login

Write-Host "? Container created!"
```

---

### If Source Files Missing (Step 5 failed):
This means your application didn't upload files successfully. Check:

1. **Application logs** for upload errors
2. **Your app's storage account configuration** in appsettings.json
3. **Your app's managed identity permissions** (it also needs Storage Blob Data Contributor)

---

## After Making Changes

### Wait for Propagation
If you just assigned permissions or enabled managed identity:
```powershell
Write-Host "??  Waiting for Azure permission propagation..."
Start-Sleep -Seconds 300  # Wait 5 minutes
Write-Host "? Propagation time complete - try again now"
```

### Verify the Fix
1. **Restart your application** (to get fresh credentials)
2. **Submit a new translation job**
3. **Check the logs** for:
   ```
   Initial operation status: NotStarted (or Running)
   ```
   NOT "ValidationFailed"

---

## Full Setup Script (Run All at Once)

```powershell
# ============================================
# FULL SETUP - Replace these values first!
# ============================================
$translationService = "YOUR_TRANSLATION_SERVICE_NAME"
$storageAccount = "doctranslationstoragecbo"
$resourceGroup = "YOUR_RESOURCE_GROUP"
$subscriptionId = "YOUR_SUBSCRIPTION_ID"
$containerName = "translations"  # From appsettings.json: "AzureBlobStorage:ContainerName"

Write-Host "?? Starting Azure Translation Service setup..."
Write-Host ""

# Step 1: Enable Managed Identity
Write-Host "1??  Enabling managed identity..."
az cognitiveservices account identity assign `
    --name $translationService `
    --resource-group $resourceGroup

# Get Principal ID
$principalId = az cognitiveservices account identity show `
    --name $translationService `
    --resource-group $resourceGroup `
    --query principalId -o tsv

Write-Host "   ? Managed Identity enabled"
Write-Host "   ?? Principal ID: $principalId"
Write-Host ""

# Step 2: Grant Storage Permissions
Write-Host "2??  Granting Storage Blob Data Contributor role..."
az role assignment create `
    --role "Storage Blob Data Contributor" `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccount" `
    --output none

Write-Host "   ? Role assigned"
Write-Host ""

# Step 3: Enable Azure Services Bypass
Write-Host "3??  Configuring storage account firewall..."
az storage account update `
    --name $storageAccount `
    --resource-group $resourceGroup `
    --bypass AzureServices `
    --output none

Write-Host "   ? Firewall configured"
Write-Host ""

# Step 4: Ensure Container Exists
Write-Host "4??  Ensuring container exists..."
az storage container create `
    --name $containerName `
    --account-name $storageAccount `
    --auth-mode login `
    --output none 2>$null

Write-Host "   ? Container ready"
Write-Host ""

# Verification
Write-Host "5??  Verifying setup..."
$roleCheck = az role assignment list `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccount" `
    --query "[?roleDefinitionName=='Storage Blob Data Contributor'].roleDefinitionName" -o tsv

if ($roleCheck -eq "Storage Blob Data Contributor") {
    Write-Host "   ? Role assignment verified"
} else {
    Write-Host "   ??  Role assignment not found (may be propagating)"
}

Write-Host ""
Write-Host "============================================"
Write-Host "? Setup Complete!"
Write-Host "============================================"
Write-Host ""
Write-Host "??  IMPORTANT: Wait 5-10 minutes for permissions to propagate"
Write-Host "?? Then restart your application"
Write-Host "?? Then test a translation job"
Write-Host ""
Write-Host "Principal ID: $principalId"
Write-Host "Storage Account: $storageAccount"
Write-Host "Container: $containerName"
```

---

## Alternative: Quick Test with SAS Tokens

If you can't wait for managed identity propagation, temporarily use SAS tokens:

### Generate a Test SAS Token
```powershell
# Generate a 48-hour SAS token for the container
$end = (Get-Date).AddHours(48).ToString("yyyy-MM-ddTHH:mm:ssZ")

az storage container generate-sas `
    --name $containerName `
    --account-name $storageAccount `
    --permissions rlw `
    --expiry $end `
    --auth-mode login `
    --output tsv
```

This will output a SAS token like:
```
sv=2021-08-06&ss=b&srt=sco&sp=rlw&se=2024-12-31T23:59:59Z&st=2024-01-01T00:00:00Z&spr=https&sig=LONG_SIGNATURE
```

**Note:** To use this, you'd need to modify your code to append this token to your URIs. See `INVALID_DOCUMENT_ACCESS_LEVEL_FIX.md` for code changes.

---

## Summary Checklist

Before testing your translation:

- [ ] Managed identity is enabled on Translation Service
- [ ] Storage Blob Data Contributor role is assigned
- [ ] Container "translations" exists in storage account (matches appsettings.json)
- [ ] Storage firewall allows Azure services
- [ ] Waited 5-10 minutes after permission changes
- [ ] Application has been restarted
- [ ] Source files upload successfully

If all checkboxes are ?, your translations should work!

---

## Still Not Working?

1. **Check application logs** for the exact error and URIs being used
2. **Run all diagnostic commands** above and save the output
3. **Verify container name** in logs matches "translations" from appsettings.json
4. **Contact Azure Support** with:
   - Translation Service name: `$translationService`
   - Storage Account name: `$storageAccount`
   - Container name: `translations`
   - Principal ID: `$principalId`
   - Job ID that failed
   - Output from all diagnostic commands above

---

## Quick Links

- See `INVALID_DOCUMENT_ACCESS_LEVEL_FIX.md` for detailed explanation
- See `PERMISSION_SETUP_COMMANDS.md` for manual step-by-step guide
- See Azure Portal for visual setup guide
