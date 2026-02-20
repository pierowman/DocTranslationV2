# Container Name Configuration Reference

## ?? Important Configuration Note

All PowerShell scripts have been updated to use the correct container name from your `appsettings.json`.

---

## Configuration Source

### appsettings.json
```json
{
  "AzureBlobStorage": {
    "AccountName": "",
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": "",
    "ContainerName": "translations"  ? YOUR CONTAINER NAME
  }
}
```

---

## PowerShell Scripts - Container Name

All scripts now use:
```powershell
$containerName = "translations"  # Matches appsettings.json
```

**NOT:**
```powershell
$containerName = "doctranslation"  # ? OLD/INCORRECT
```

---

## Expected URIs in Logs

When your application runs, you should see URIs like:

```
? CORRECT:
https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/source/file.pdf

? INCORRECT:
https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc-123/source/file.pdf
```

---

## Verification Commands

### Check if container exists:
```powershell
az storage container show `
    --name translations `
    --account-name doctranslationstoragecbo `
    --auth-mode login
```

### Create container if it doesn't exist:
```powershell
az storage container create `
    --name translations `
    --account-name doctranslationstoragecbo `
    --auth-mode login
```

### List blobs in correct container:
```powershell
az storage blob list `
    --container-name translations `
    --account-name doctranslationstoragecbo `
    --prefix "jobs/" `
    --auth-mode login `
    --output table
```

---

## Files Updated

All of these files now use **"translations"** as the container name:

1. ? `RUN_THESE_COMMANDS_NOW.md`
2. ? `QUICK_TROUBLESHOOTING_CHECKLIST.md`
3. ? `COMPLETE_FIX_GUIDE.md`

---

## Configuration Consistency Checklist

Verify these all match:

- [ ] **appsettings.json** ? `"ContainerName": "translations"`
- [ ] **appsettings.Development.json** ? `"ContainerName": "translations"` (if overridden)
- [ ] **Azure Storage Account** ? Container named "translations" exists
- [ ] **PowerShell scripts** ? `$containerName = "translations"`
- [ ] **Application logs** ? URIs show `.../translations/jobs/...`

If all match, your configuration is consistent! ?

---

## Quick Test

Run this to verify your container exists with the correct name:

```powershell
$storageAccount = "doctranslationstoragecbo"
$containerName = "translations"

Write-Host "Checking container: $containerName" -ForegroundColor Yellow

$exists = az storage container exists `
    --name $containerName `
    --account-name $storageAccount `
    --auth-mode login `
    --query exists `
    --output tsv

if ($exists -eq "true") {
    Write-Host "? Container '$containerName' exists and is ready!" -ForegroundColor Green
} else {
    Write-Host "? Container '$containerName' does not exist!" -ForegroundColor Red
    Write-Host "Creating container..." -ForegroundColor Yellow
    
    az storage container create `
        --name $containerName `
        --account-name $storageAccount `
        --auth-mode login
    
    Write-Host "? Container '$containerName' created!" -ForegroundColor Green
}
```

---

## Summary

**Container Name:** `translations`  
**Source:** `appsettings.json` ? `AzureBlobStorage:ContainerName`  
**All scripts updated:** ?  
**Consistency verified:** Make sure Azure container exists with this exact name
