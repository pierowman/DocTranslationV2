# FIX InvalidDocumentAccessLevel - Run These Commands Now

## The Problem
Your Azure Translation Service cannot access blob storage because of missing permissions.

Error: `InvalidDocumentAccessLevel - "Cannot access source document location with the current permissions."`

## The Solution
Run these PowerShell commands to fix it.

---

## STEP 1: Set Your Variables

**Replace these with your actual values:**

```powershell
# === REPLACE THESE VALUES ===
$translationService = "translationcbo"            # Your Translation Service name
$storageAccount = "doctranslationstoragecbo"     # Your Storage Account name  
$resourceGroup = "YOUR_RESOURCE_GROUP_NAME"      # Your Resource Group name
$subscriptionId = "YOUR_SUBSCRIPTION_ID"         # Your Azure Subscription ID

# No need to change these (matches your appsettings.json)
$containerName = "translations"  # This matches "AzureBlobStorage:ContainerName" in appsettings.json
$roleName = "Storage Blob Data Contributor"
```

### How to Find These Values:

#### Translation Service Name:
```powershell
# List all translation services
az cognitiveservices account list --query "[?kind=='TranslatorText'].{Name:name, ResourceGroup:resourceGroup}" --output table
```

#### Storage Account Name:
```powershell
# List all storage accounts
az storage account list --query "[].{Name:name, ResourceGroup:resourceGroup}" --output table
```

#### Subscription ID:
```powershell
# Get your current subscription
az account show --query id --output tsv
```

---

## STEP 2: Enable Managed Identity

```powershell
Write-Host "Enabling Managed Identity on Translation Service..." -ForegroundColor Yellow

az cognitiveservices account identity assign `
    --name $translationService `
    --resource-group $resourceGroup

Write-Host "? Done!" -ForegroundColor Green
Write-Host ""
```

---

## STEP 3: Get Principal ID

```powershell
Write-Host "Getting Principal ID..." -ForegroundColor Yellow

$principalId = az cognitiveservices account identity show `
    --name $translationService `
    --resource-group $resourceGroup `
    --query principalId `
    --output tsv

if ([string]::IsNullOrEmpty($principalId)) {
    Write-Host "? ERROR: Could not get Principal ID" -ForegroundColor Red
    Write-Host "   Managed Identity may not be enabled yet." -ForegroundColor Red
    Write-Host "   Wait 30 seconds and try STEP 2 and STEP 3 again." -ForegroundColor Red
    exit
}

Write-Host "? Principal ID: $principalId" -ForegroundColor Green
Write-Host ""
```

---

## STEP 4: Grant Storage Permissions

```powershell
Write-Host "Granting Storage Blob Data Contributor role..." -ForegroundColor Yellow

$scope = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccount"

az role assignment create `
    --role $roleName `
    --assignee $principalId `
    --scope $scope

Write-Host "? Done!" -ForegroundColor Green
Write-Host ""
```

---

## STEP 5: Verify Setup

```powershell
Write-Host "Verifying role assignment..." -ForegroundColor Yellow

$roleCheck = az role assignment list `
    --assignee $principalId `
    --scope $scope `
    --query "[?roleDefinitionName=='$roleName'].roleDefinitionName" `
    --output tsv

if ($roleCheck -eq $roleName) {
    Write-Host "? Role assignment VERIFIED: $roleName" -ForegroundColor Green
} else {
    Write-Host "??  Role assignment not found yet (still propagating)" -ForegroundColor Yellow
}

Write-Host ""
```

---

## STEP 6: Configure Storage Firewall (If Needed)

```powershell
Write-Host "Checking storage firewall..." -ForegroundColor Yellow

$firewallStatus = az storage account show `
    --name $storageAccount `
    --resource-group $resourceGroup `
    --query "networkRuleSet.defaultAction" `
    --output tsv

Write-Host "Current firewall setting: $firewallStatus"

if ($firewallStatus -eq "Deny") {
    Write-Host "??  Firewall is active - configuring to allow Azure services..." -ForegroundColor Yellow
    
    az storage account update `
        --name $storageAccount `
        --resource-group $resourceGroup `
        --bypass AzureServices
    
    Write-Host "? Firewall configured to allow Azure services" -ForegroundColor Green
} else {
    Write-Host "? Firewall allows access" -ForegroundColor Green
}

Write-Host ""
```

---

## STEP 7: Verify Container Exists

```powershell
Write-Host "Checking if container exists..." -ForegroundColor Yellow

$containerExists = az storage container exists `
    --name $containerName `
    --account-name $storageAccount `
    --auth-mode login `
    --query exists `
    --output tsv

if ($containerExists -eq "true") {
    Write-Host "? Container '$containerName' exists" -ForegroundColor Green
} else {
    Write-Host "??  Container '$containerName' does not exist - creating..." -ForegroundColor Yellow
    
    az storage container create `
        --name $containerName `
        --account-name $storageAccount `
        --auth-mode login
    
    Write-Host "? Container created" -ForegroundColor Green
}

Write-Host ""
```

---

## STEP 8: Wait for Propagation

```powershell
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "? SETUP COMPLETE!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "??  IMPORTANT: Wait 5-10 minutes for Azure to propagate permissions" -ForegroundColor Yellow
Write-Host ""
Write-Host "Then:" -ForegroundColor Cyan
Write-Host "  1. Restart your application" -ForegroundColor White
Write-Host "  2. Submit a test translation job" -ForegroundColor White
Write-Host "  3. Check that you see 'NotStarted' or 'Running' (NOT 'ValidationFailed')" -ForegroundColor White
Write-Host ""
Write-Host "Configuration Summary:" -ForegroundColor Cyan
Write-Host "  Translation Service: $translationService" -ForegroundColor White
Write-Host "  Storage Account: $storageAccount" -ForegroundColor White
Write-Host "  Container: $containerName" -ForegroundColor White
Write-Host "  Principal ID: $principalId" -ForegroundColor White
Write-Host "  Role: $roleName" -ForegroundColor White
Write-Host ""
```

---

## COMPLETE SCRIPT (Copy & Run All at Once)

```powershell
# ============================================
# FIX: InvalidDocumentAccessLevel Error
# Run this entire script to set up permissions
# ============================================

# === STEP 1: SET YOUR VALUES ===
$translationService = "translationcbo"            # CHANGE THIS
$storageAccount = "doctranslationstoragecbo"     # CHANGE THIS
$resourceGroup = "YOUR_RESOURCE_GROUP"           # CHANGE THIS
$subscriptionId = "YOUR_SUBSCRIPTION_ID"         # CHANGE THIS

# Constants (don't change - matches appsettings.json)
$containerName = "translations"  # This matches "AzureBlobStorage:ContainerName" in appsettings.json
$roleName = "Storage Blob Data Contributor"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Starting Azure Translation Service Setup" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# === STEP 2: Enable Managed Identity ===
Write-Host "1. Enabling Managed Identity..." -ForegroundColor Yellow
az cognitiveservices account identity assign `
    --name $translationService `
    --resource-group $resourceGroup `
    --output none
Write-Host "   ? Done" -ForegroundColor Green
Write-Host ""

# === STEP 3: Get Principal ID ===
Write-Host "2. Getting Principal ID..." -ForegroundColor Yellow
$principalId = az cognitiveservices account identity show `
    --name $translationService `
    --resource-group $resourceGroup `
    --query principalId `
    --output tsv

if ([string]::IsNullOrEmpty($principalId)) {
    Write-Host "   ? ERROR: Could not get Principal ID" -ForegroundColor Red
    Write-Host "   Wait 30 seconds and run this script again." -ForegroundColor Red
    exit
}
Write-Host "   ? Principal ID: $principalId" -ForegroundColor Green
Write-Host ""

# === STEP 4: Grant Storage Permissions ===
Write-Host "3. Granting Storage Permissions..." -ForegroundColor Yellow
$scope = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccount"

az role assignment create `
    --role $roleName `
    --assignee $principalId `
    --scope $scope `
    --output none 2>$null  # Suppress "already exists" warnings

Write-Host "   ? Role assigned" -ForegroundColor Green
Write-Host ""

# === STEP 5: Verify ===
Write-Host "4. Verifying setup..." -ForegroundColor Yellow
$roleCheck = az role assignment list `
    --assignee $principalId `
    --scope $scope `
    --query "[?roleDefinitionName=='$roleName'].roleDefinitionName" `
    --output tsv

if ($roleCheck -eq $roleName) {
    Write-Host "   ? Verified: Role assignment exists" -ForegroundColor Green
} else {
    Write-Host "   ??  Role not found yet (propagating...)" -ForegroundColor Yellow
}
Write-Host ""

# === STEP 6: Configure Firewall ===
Write-Host "5. Configuring storage firewall..." -ForegroundColor Yellow
az storage account update `
    --name $storageAccount `
    --resource-group $resourceGroup `
    --bypass AzureServices `
    --output none
Write-Host "   ? Firewall configured" -ForegroundColor Green
Write-Host ""

# === STEP 7: Ensure Container ===
Write-Host "6. Ensuring container exists..." -ForegroundColor Yellow
az storage container create `
    --name $containerName `
    --account-name $storageAccount `
    --auth-mode login `
    --output none 2>$null  # Suppress "already exists" warnings
Write-Host "   ? Container ready" -ForegroundColor Green
Write-Host ""

# === STEP 8: Summary ===
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "? SETUP COMPLETE!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "??  Wait 5-10 minutes, then:" -ForegroundColor Yellow
Write-Host "   1. Restart your application" -ForegroundColor White
Write-Host "   2. Test a translation job" -ForegroundColor White
Write-Host "   3. Should see 'NotStarted' or 'Running' status" -ForegroundColor White
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Cyan
Write-Host "  Translation: $translationService" -ForegroundColor White
Write-Host "  Storage: $storageAccount" -ForegroundColor White
Write-Host "  Container: $containerName" -ForegroundColor White
Write-Host "  Principal: $principalId" -ForegroundColor White
Write-Host "  Role: $roleName" -ForegroundColor White
Write-Host ""
```

---

## After Running the Script

### Wait 5-10 Minutes
Azure needs time to propagate the permissions across all services.

### Restart Your Application
```powershell
# If running in Visual Studio, stop and restart
# If running via dotnet, press Ctrl+C and restart
```

### Test a Translation
1. Upload a test document
2. Select a target language
3. Click "Translate"
4. **Check the logs** for:
   ```
   Initial operation status: NotStarted
   ```
   OR
   ```
   Initial operation status: Running
   ```

### Success Indicators
? Status is **NotStarted** or **Running** (not ValidationFailed)
? No more "InvalidDocumentAccessLevel" errors
? Job progresses to **Succeeded** after processing

---

## If It Still Doesn't Work

### Check the Logs
Look for this specific line:
```
Translation input - Source: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/...
```

The URI should:
- Start with `https://`
- Include your storage account name
- Include the container name: **translations**
- Include the job path
- **NOT** have `?sv=...` query parameters (that would indicate SAS tokens)

### Verify Permissions Again
```powershell
# Run this to double-check
az role assignment list `
    --assignee $principalId `
    --scope $scope `
    --output table
```

Should show:
| PrincipalName | Role | Scope |
|---------------|------|-------|
| translationcbo | Storage Blob Data Contributor | .../storageAccounts/doctranslationstoragecbo |

### Check Application Insights
If you have Application Insights enabled:
1. Go to Azure Portal ? Application Insights
2. Look for "Exceptions" or "Failed requests"
3. Filter by your Translation Service requests
4. Look for detailed error messages

---

## Alternative: Test with SAS Tokens First

If you want to verify your code works while waiting for permissions:

```powershell
# Generate a 48-hour SAS token
$sasToken = az storage container generate-sas `
    --name $containerName `
    --account-name $storageAccount `
    --permissions rlw `
    --expiry (Get-Date).AddHours(48).ToString("yyyy-MM-ddTHH:mm:ssZ") `
    --auth-mode login `
    --output tsv

Write-Host "SAS Token (valid for 48 hours):"
Write-Host $sasToken
```

Then modify your code to append `?$sasToken` to your URIs (see `INVALID_DOCUMENT_ACCESS_LEVEL_FIX.md` for code changes).

---

## Summary

**Problem:** `InvalidDocumentAccessLevel` error  
**Cause:** Translation Service can't access blob storage  
**Fix:** Grant "Storage Blob Data Contributor" role  
**Container:** **translations** (matches appsettings.json)  
**Time:** 5-10 minutes for permissions to propagate  

**Run the complete script above, wait 10 minutes, restart your app, and test!**
