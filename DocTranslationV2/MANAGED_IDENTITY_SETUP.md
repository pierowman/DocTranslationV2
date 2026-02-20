# Managed Identity Permissions Setup Guide

## Overview

This application uses **Azure Managed Identity** for secure, keyless authentication. Two managed identities need access to blob storage:

1. **Web Application Managed Identity** - for uploading files and managing jobs
2. **Translation Service Managed Identity** - for reading source files and writing translated files

## Prerequisites

- Azure CLI installed and logged in
- Owner or User Access Administrator role on the resource group
- Resources already created:
  - Web App / App Service
  - Cognitive Services Translation resource
  - Storage Account

## Step-by-Step Setup

### Step 1: Enable Managed Identity on Translation Service

```bash
# Enable system-assigned managed identity
az cognitiveservices account identity assign \
    --name YOUR_TRANSLATION_SERVICE_NAME \
    --resource-group YOUR_RESOURCE_GROUP

# Example:
az cognitiveservices account identity assign \
    --name translationcbo \
    --resource-group DocTranslation-RG
```

**Expected output:**
```json
{
  "principalId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "type": "SystemAssigned"
}
```

Save the `principalId` - you'll need it in the next step.

### Step 2: Grant Translation Service Access to Storage

```bash
# Replace with your values
TRANSLATION_PRINCIPAL_ID="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"  # From Step 1
SUBSCRIPTION_ID="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
RESOURCE_GROUP="YOUR_RESOURCE_GROUP"
STORAGE_ACCOUNT="YOUR_STORAGE_ACCOUNT"

# Grant Storage Blob Data Contributor role
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee $TRANSLATION_PRINCIPAL_ID \
    --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT

# Example:
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee "12345678-1234-1234-1234-123456789abc" \
    --scope /subscriptions/abc-def-ghi/resourceGroups/DocTranslation-RG/providers/Microsoft.Storage/storageAccounts/doctranslationstorage
```

**Expected output:**
```json
{
  "principalId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "principalName": "translationcbo",
  "roleDefinitionName": "Storage Blob Data Contributor",
  "scope": "/subscriptions/.../storageAccounts/doctranslationstorage"
}
```

### Step 3: Enable Managed Identity on Web App (if not already done)

```bash
# Enable system-assigned managed identity for web app
az webapp identity assign \
    --name YOUR_WEB_APP_NAME \
    --resource-group YOUR_RESOURCE_GROUP

# Example:
az webapp identity assign \
    --name DocTranslationApp \
    --resource-group DocTranslation-RG
```

Save the `principalId` from the output.

### Step 4: Grant Web App Access to Storage

```bash
# Replace with your values
APP_PRINCIPAL_ID="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"  # From Step 3

# Grant Storage Blob Data Contributor role
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee $APP_PRINCIPAL_ID \
    --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT
```

### Step 5: Grant Web App Access to Translation Service (if using managed identity for translation calls)

```bash
# Get Translation Service resource ID
TRANSLATION_ID=$(az cognitiveservices account show \
    --name YOUR_TRANSLATION_SERVICE_NAME \
    --resource-group YOUR_RESOURCE_GROUP \
    --query id -o tsv)

# Grant Cognitive Services User role
az role assignment create \
    --role "Cognitive Services User" \
    --assignee $APP_PRINCIPAL_ID \
    --scope $TRANSLATION_ID
```

### Step 6: Verify Permissions

```bash
# Check Translation Service has storage access
az role assignment list \
    --assignee $TRANSLATION_PRINCIPAL_ID \
    --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT \
    --query "[?roleDefinitionName=='Storage Blob Data Contributor']"

# Check Web App has storage access
az role assignment list \
    --assignee $APP_PRINCIPAL_ID \
    --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT \
    --query "[?roleDefinitionName=='Storage Blob Data Contributor']"
```

**Both should return a non-empty array.** If empty, the permission isn't set.

## Verification Checklist

- [ ] Translation Service has system-assigned managed identity enabled
- [ ] Translation Service has "Storage Blob Data Contributor" role on storage account
- [ ] Web App has system-assigned managed identity enabled
- [ ] Web App has "Storage Blob Data Contributor" role on storage account
- [ ] Web App has "Cognitive Services User" role on translation service (if using managed identity)
- [ ] Waited 5-10 minutes for role assignments to propagate

## Testing Access

### Test Translation Service Can Access Storage

```bash
# Try to list blobs (should work after permissions are set)
# Note: This tests from YOUR account, not the managed identity
# The real test is running a translation job

# Check if translation service identity exists
az cognitiveservices account identity show \
    --name YOUR_TRANSLATION_SERVICE_NAME \
    --resource-group YOUR_RESOURCE_GROUP
```

### Test Web App Can Access Storage

```bash
# From the web app's Kudu console (Advanced Tools > Kudu > Debug console)
# Run: az storage blob list --account-name YOUR_STORAGE --auth-mode login
```

Or test by uploading a file through the application UI.

## Common Issues

### Issue: "Jobs still fail validation after setting permissions"

**Solution**: Wait 5-10 minutes. Role assignments take time to propagate through Azure AD.

### Issue: "RequestFailedException: Status=403 (Forbidden)"

**Solution**: 
1. Verify managed identity is enabled
2. Check role assignment exists
3. Wait for propagation
4. Ensure you're using the correct scope (storage account level)

### Issue: "NullReferenceException when checking status"

**Solution**: This is a separate issue from permissions. See the main fix documentation.

### Issue: "Cannot find managed identity principal ID"

**Solution**:
```bash
# For Translation Service
az cognitiveservices account identity show \
    --name YOUR_TRANSLATION_SERVICE \
    --resource-group YOUR_RG \
    --query principalId -o tsv

# For Web App
az webapp identity show \
    --name YOUR_WEB_APP \
    --resource-group YOUR_RG \
    --query principalId -o tsv
```

## Application Configuration

Ensure your `appsettings.json` has the storage account name configured:

```json
{
  "TranslationConfiguration": {
    "AzureBlobStorage": {
      "AccountName": "YOUR_STORAGE_ACCOUNT_NAME",
      "ContainerName": "translations",
      "TenantId": "YOUR_TENANT_ID",
      "ClientId": "YOUR_CLIENT_ID",
      "ClientSecret": "YOUR_CLIENT_SECRET"
    }
  }
}
```

**Note**: If using managed identity in production, you can remove `TenantId`, `ClientId`, and `ClientSecret` - the app will automatically use the managed identity.

## Roles Explained

### Storage Blob Data Contributor
- **Purpose**: Full access to blob containers and data
- **Permissions**: Read, write, delete blobs
- **Why needed**: 
  - App needs to upload source files
  - Translation Service needs to read source and write translated files

### Cognitive Services User
- **Purpose**: Call Cognitive Services APIs
- **Permissions**: Use translation endpoints
- **Why needed**: App makes calls to Translation Service API

## Architecture Diagram

```
???????????????????????????????????????????????????????????
?                    Azure AD                             ?
?  ???????????????????         ???????????????????      ?
?  ? Web App         ?         ? Translation     ?      ?
?  ? Managed         ?         ? Service         ?      ?
?  ? Identity        ?         ? Managed         ?      ?
?  ???????????????????         ? Identity        ?      ?
?           ?                  ???????????????????      ?
?????????????????????????????????????????????????????????
            ?                           ?
            ? Both have                 ?
            ? "Storage Blob Data        ?
            ?  Contributor" role        ?
            ?                           ?
            ?????????????????????????????
                        ?
                        ?
            ?????????????????????????
            ?   Blob Storage        ?
            ?   - Source files      ?
            ?   - Translated files  ?
            ?????????????????????????
```

## Next Steps

After completing this setup:
1. Wait 5-10 minutes for permissions to propagate
2. Test by uploading a document for translation
3. Monitor Azure Translation Service queue
4. Check Application Insights for any permission errors
5. Verify translated files appear in blob storage

## Troubleshooting Commands

```bash
# List all role assignments for a principal
az role assignment list --assignee PRINCIPAL_ID --all

# List all role assignments on storage account
az role assignment list \
    --scope /subscriptions/YOUR_SUB/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE

# Delete a role assignment (if you made a mistake)
az role assignment delete \
    --assignee PRINCIPAL_ID \
    --role "Storage Blob Data Contributor" \
    --scope /subscriptions/.../storageAccounts/YOUR_STORAGE

# Check if managed identity is enabled
az cognitiveservices account identity show --name YOUR_TRANSLATION --resource-group YOUR_RG
az webapp identity show --name YOUR_APP --resource-group YOUR_RG
```

## Security Best Practices

? **DO**:
- Use managed identity instead of connection strings
- Assign roles at the most specific scope (storage account level)
- Use system-assigned identity when possible
- Regularly audit role assignments

? **DON'T**:
- Store connection strings or keys in code
- Grant more permissions than needed (use least privilege)
- Assign roles at subscription level unless necessary
- Share managed identity principal IDs publicly
