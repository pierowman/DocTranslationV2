# InvalidDocumentAccessLevel Error - Complete Guide

## Quick Summary

**Error:** `InvalidDocumentAccessLevel - "Cannot access source document location with the current permissions."`

**Meaning:** Your Azure Translation Service cannot authenticate to your blob storage.

**Fix:** Enable managed identity and grant storage permissions.

**Time Required:** 5-10 minutes (including waiting for Azure permission propagation)

**Container Name:** **translations** (as defined in your appsettings.json)

---

## Three Files to Help You

1. **`RUN_THESE_COMMANDS_NOW.md`** ?
   - **START HERE** - Copy/paste PowerShell commands
   - Fixes the problem in 5 minutes
   - Complete automated script

2. **`QUICK_TROUBLESHOOTING_CHECKLIST.md`** ??
   - Diagnostic commands to identify the issue
   - Step-by-step verification
   - Decision tree for different scenarios

3. **`INVALID_DOCUMENT_ACCESS_LEVEL_FIX.md`** ??
   - Detailed explanation of the error
   - Two solutions (Managed Identity & SAS Tokens)
   - Complete troubleshooting guide

---

## The Problem Explained Simply

```
Your Application
      ?
Uploads files to Blob Storage ? (works - your app has permissions)
      ?
Tells Translation Service: "Translate these files at this URL"
      ?
Translation Service tries to read from Blob Storage ? (FAILS - no permissions)
      ?
Error: InvalidDocumentAccessLevel
```

**The Issue:** The Translation Service and your application are **two different Azure resources** with **separate identities**.

Your app can access storage (because you configured it), but the Translation Service cannot.

---

## The Fix (High Level)

1. **Give the Translation Service an identity** (called "managed identity")
2. **Grant that identity permission** to read/write blobs in your storage account
3. **Wait** 5-10 minutes for Azure to propagate the permissions
4. **Test** - it should work now

---

## Step-by-Step Fix

### Option 1: Use the Automated Script (Recommended)

1. Open **`RUN_THESE_COMMANDS_NOW.md`**
2. Replace the 4 variables at the top with your values
3. **Verify container name is "translations"** (matches appsettings.json)
4. Copy the entire "COMPLETE SCRIPT" section
5. Paste into **PowerShell**
6. Press **Enter**
7. **Wait 10 minutes**
8. **Restart your application**
9. **Test a translation**

### Option 2: Manual Azure Portal Setup

1. Open **Azure Portal**
2. Go to your **Translation Service** resource
3. Click **Identity** ? **System assigned** ? Toggle **On** ? **Save**
4. Go to your **Storage Account** resource
5. Click **Access Control (IAM)** ? **Add role assignment**
6. Select role: **Storage Blob Data Contributor**
7. Assign to: Your Translation Service (search by name)
8. Click **Save**
9. **Wait 10 minutes**
10. **Restart your application**
11. **Test a translation**

---

## How to Know It's Fixed

### In Your Application Logs

**Before (Error):**
```
Initial operation status: ValidationFailed
Could not update status after initial delay
Azure.RequestFailedException: Cannot access source document location
ErrorCode: InvalidRequest
InnerError: InvalidDocumentAccessLevel
```

**After (Success):**
```
Translation input - Source: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/...
Batch translation started with operation ID: abc-123-def-456
Initial operation status: NotStarted
After 5 second delay - Status: Running
Document counts after delay - Total: 1, Succeeded: 0, Failed: 0, NotStarted: 1
```

**Note:** The URI should include **/translations/** (your container name from appsettings.json)

### In Azure Portal

1. Go to **Translation Service** ? **Document Translation**
2. Find your job
3. **Status should be:** "Running" or "Succeeded" (NOT "ValidationFailed")

---

## Common Issues

### "I ran the script but still getting the error"

**Causes:**
- ? Didn't wait 5-10 minutes for propagation
- ? Didn't restart the application
- ? Storage account has firewall blocking the service
- ? Container name mismatch

**Solutions:**
- ? Wait longer (can take up to 10 minutes)
- ? Stop and restart your application (don't just rebuild)
- ? Check storage firewall settings (see troubleshooting checklist)
- ? Verify container name is "translations" in both appsettings.json and Azure

### "Script says 'Role assignment not found yet'"

**This is normal!** It means:
- ? The role was assigned
- ?? But Azure hasn't propagated it everywhere yet
- ?? Wait 5-10 minutes and it will work

### "principalId is null or empty"

**Causes:**
- ? Managed identity wasn't enabled
- ? Just enabled it but Azure hasn't created it yet

**Solutions:**
- ? Wait 30 seconds
- ? Run the script again

### "Container 'translations' not found"

**Cause:**
- ? Container doesn't exist in your storage account

**Solution:**
```powershell
az storage container create `
    --name translations `
    --account-name doctranslationstoragecbo `
    --auth-mode login
```

### "I don't know my Resource Group or Subscription ID"

**Find Resource Group:**
```powershell
az cognitiveservices account list --query "[?kind=='TranslatorText'].{Name:name, ResourceGroup:resourceGroup}" --output table
```

**Find Subscription ID:**
```powershell
az account show --query id --output tsv
```

---

## What the Fix Does

### Before Setup:
```
Translation Service Identity: NONE ?
Storage Permissions: NONE ?
Result: Cannot access storage ?
```

### After Setup:
```
Translation Service Identity: System-assigned managed identity ?
Storage Permissions: Storage Blob Data Contributor role ?
Container: translations (matches appsettings.json) ?
Result: Can read/write blobs ?
```

### Why This Is Secure:
- ? No passwords or keys in your code
- ? No SAS tokens that can expire or leak
- ? Azure automatically rotates credentials
- ? Centralized permission management in IAM
- ? Full audit trail of access

---

## Alternative: Quick Fix with SAS Tokens

If you:
- Can't modify Azure permissions (limited access)
- Need it working immediately (can't wait 10 minutes)
- Want to test while waiting for permission propagation

Then you can use **SAS tokens** instead:
- See **`INVALID_DOCUMENT_ACCESS_LEVEL_FIX.md`** ? "Solution 2"
- Requires code changes in `DocumentTranslationService.cs`
- Less secure (tokens exposed in URLs)
- Tokens expire and need regeneration

**Recommendation:** Use managed identity for production, SAS tokens only for quick testing.

---

## Verification Commands

### Check if managed identity is enabled:
```powershell
az cognitiveservices account identity show --name translationcbo --resource-group YOUR_RG
```

### Check if permissions are assigned:
```powershell
az role assignment list --assignee YOUR_PRINCIPAL_ID --scope /subscriptions/SUB/resourceGroups/RG/providers/Microsoft.Storage/storageAccounts/STORAGE
```

### Check if container exists:
```powershell
az storage container show --name translations --account-name doctranslationstoragecbo --auth-mode login
```

### Check if files were uploaded:
```powershell
az storage blob list --container-name translations --account-name doctranslationstoragecbo --prefix "jobs/" --auth-mode login --output table
```

---

## Complete File Reference

| File | Purpose | When to Use |
|------|---------|-------------|
| **RUN_THESE_COMMANDS_NOW.md** | Automated fix script | **Use this first** - fastest solution |
| **QUICK_TROUBLESHOOTING_CHECKLIST.md** | Diagnostic commands | When you need to verify what's wrong |
| **INVALID_DOCUMENT_ACCESS_LEVEL_FIX.md** | Complete explanation | When you want to understand the details |
| **PERMISSION_SETUP_COMMANDS.md** | Manual setup guide | When you prefer step-by-step instructions |

---

## Success Checklist

After running the fix, verify all of these:

- [ ] Managed identity is enabled on Translation Service
- [ ] Principal ID is visible (not null)
- [ ] Storage Blob Data Contributor role is assigned
- [ ] Role assignment appears in `az role assignment list`
- [ ] Storage firewall allows Azure services
- [ ] Container "translations" exists (matches appsettings.json)
- [ ] Waited 5-10 minutes after permission changes
- [ ] Application was restarted (full restart, not just rebuild)
- [ ] Test translation shows status "NotStarted" or "Running"
- [ ] No "InvalidDocumentAccessLevel" errors in logs
- [ ] URIs in logs show "/translations/" (correct container name)

If all checkboxes are ?, your translations will work!

---

## What Happens After the Fix

### Your translation workflow will be:

1. **User uploads document** ? Saved to blob storage (container: translations) ?
2. **Application calls Translation Service** ? Passes blob storage URIs ?
3. **Translation Service reads source files** ? Using managed identity ? (This is what was failing before)
4. **Translation Service translates content** ? Uses Azure AI ?
5. **Translation Service writes results** ? Back to blob storage (container: translations) ? (This also needs permissions)
6. **User downloads translated file** ? From blob storage ?

**Before the fix:** Step 3 failed with `InvalidDocumentAccessLevel`  
**After the fix:** All steps work seamlessly ?

---

## Support

### If You're Still Stuck:

1. **Run all diagnostic commands** from `QUICK_TROUBLESHOOTING_CHECKLIST.md`
2. **Save all command outputs**
3. **Check your application logs** for exact error messages
4. **Verify container name** - should be "translations" in both:
   - appsettings.json: `"AzureBlobStorage:ContainerName": "translations"`
   - Azure Storage Account: Container named "translations"
   - Application logs: URIs should show `.../translations/jobs/...`
5. **Note the URIs** being logged in "Translation input - Source:" messages
6. **Contact Azure Support** with:
   - Your Translation Service resource ID
   - Your Storage Account resource ID
   - Container name: translations
   - Principal ID from managed identity
   - Job ID that failed
   - All diagnostic command outputs
   - Your application logs

### Useful Log Filters:

Look for these specific log messages:
- `Translation input - Source:` ? Shows URIs being used (should include /translations/)
- `Initial operation status:` ? Shows if validation passed
- `InvalidDocumentAccessLevel` ? The specific error
- `Could not update status` ? Indicates permission issues

---

## Summary

? **The error:** Translation Service can't access blob storage  
? **The fix:** Enable managed identity + grant permissions  
? **The time:** 5-10 minutes total (including wait time)  
? **The container:** "translations" (from appsettings.json)  
? **The files:** Three guides to help you fix it  
? **The result:** Translations work perfectly  

**Start with `RUN_THESE_COMMANDS_NOW.md` and you'll be fixed in 10 minutes!**
