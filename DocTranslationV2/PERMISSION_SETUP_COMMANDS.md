# Azure Translation Service Permission Setup

## The Error You're Seeing

```
Cannot access source document location with the current permissions.
ErrorCode: InvalidRequest
InnerError: InvalidDocumentAccessLevel
```

This error means the **Azure Translation Service cannot access your blob storage** because its managed identity doesn't have the required permissions.

## Solution: Grant Storage Access to Translation Service

### Step 1: Enable Managed Identity on Translation Service

First, ensure your Translation Service has a system-assigned managed identity enabled.

#### Using Azure Portal:
1. Go to your **Azure Cognitive Services / Translator** resource
2. Click **Identity** in the left menu
3. Under **System assigned**, toggle **Status** to **On**
4. Click **Save**
5. Note the **Object (principal) ID** that appears

#### Using Azure CLI:
```powershell
# Enable managed identity
az cognitiveservices account identity assign `
    --name YOUR_TRANSLATION_SERVICE_NAME `
    --resource-group YOUR_RESOURCE_GROUP

# Get the principal ID (save this)
az cognitiveservices account identity show `
    --name YOUR_TRANSLATION_SERVICE_NAME `
    --resource-group YOUR_RESOURCE_GROUP `
    --query principalId -o tsv
```

### Step 2: Grant Storage Blob Data Contributor Role

The Translation Service needs **"Storage Blob Data Contributor"** role on your storage account.

#### Using Azure Portal:
1. Go to your **Storage Account** (doctranslationstoragecbo)
2. Click **Access Control (IAM)** in the left menu
3. Click **+ Add** ? **Add role assignment**
4. In the **Role** tab, search for and select **"Storage Blob Data Contributor"**
5. Click **Next**
6. In the **Members** tab, click **+ Select members**
7. Search for your Translation Service name
8. Select it and click **Select**
9. Click **Review + assign**

#### Using Azure CLI:
```powershell
# Set your resource details
$translationServiceName = "YOUR_TRANSLATION_SERVICE_NAME"
$storageAccountName = "doctranslationstoragecbo"
$resourceGroup = "YOUR_RESOURCE_GROUP"
$subscriptionId = "YOUR_SUBSCRIPTION_ID"

# Get the Translation Service's principal ID
$principalId = az cognitiveservices account identity show `
    --name $translationServiceName `
    --resource-group $resourceGroup `
    --query principalId -o tsv

Write-Host "Translation Service Principal ID: $principalId"

# Grant Storage Blob Data Contributor role
az role assignment create `
    --role "Storage Blob Data Contributor" `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccountName"

Write-Host "Role assigned successfully!"
```

### Step 3: Wait for Propagation

?? **Important:** After assigning the role, wait **5-10 minutes** for the permission to propagate through Azure's systems.

### Step 4: Verify the Role Assignment

#### Using Azure Portal:
1. Go to your **Storage Account**
2. Click **Access Control (IAM)**
3. Click **Role assignments** tab
4. Search for your Translation Service name
5. Verify it has **"Storage Blob Data Contributor"** role

#### Using Azure CLI:
```powershell
# List role assignments for the Translation Service
az role assignment list `
    --assignee $principalId `
    --scope "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Storage/storageAccounts/$storageAccountName" `
    --output table
```

Expected output should show:
```
PrincipalName                    Role                           Scope
-------------------------------  -----------------------------  ----
YOUR_TRANSLATION_SERVICE         Storage Blob Data Contributor  /subscriptions/.../storageAccounts/doctranslationstoragecbo
```

## Alternative: Check All Role Assignments

If you're not sure if the role is assigned, check all roles for your Translation Service:

```powershell
az role assignment list `
    --assignee $principalId `
    --all `
    --output table
```

## Troubleshooting

### Issue: "Cannot find principal"
**Solution:** Make sure managed identity is enabled (Step 1) and wait a minute for it to propagate.

### Issue: "Insufficient permissions to assign role"
**Solution:** You need **"Owner"** or **"User Access Administrator"** role on the storage account to grant permissions.

### Issue: Still getting permission errors after 10 minutes
**Solution:** 
1. Verify the role assignment is visible in the portal
2. Check if your storage account has firewall rules - you may need to add the Translation Service to allowed networks
3. Ensure you're using the correct storage account name in your configuration

## Testing After Setup

After completing these steps and waiting for propagation:

1. Stop your application if it's running
2. Restart your application
3. Try a translation job again
4. Check the logs - you should no longer see the permission error

## What This Role Does

**"Storage Blob Data Contributor"** allows the Translation Service to:
- ? Read blobs (source documents)
- ? Write blobs (translated documents)
- ? List blobs (enumerate source files)
- ? Delete blobs (cleanup if needed)

This is exactly what the Translation Service needs to perform batch translations.

## Security Note

This approach uses **Managed Identity** which is more secure than SAS tokens because:
- ? No secrets or keys in your code
- ? Automatic credential rotation by Azure
- ? Centralized permission management in Azure IAM
- ? Audit logging of access

## Code Changes Made

The code now properly uses `BlobContainerClient.Uri` to construct URIs that work with managed identity authentication:

```csharp
var blobServiceClient = new BlobServiceClient(blobUri, _credentialService.GetBlobStorageCredential());
var containerClient = blobServiceClient.GetBlobContainerClient(_blobSettings.ContainerName);

var containerUriString = containerClient.Uri.ToString().TrimEnd('/');
var sourceUri = new Uri($"{containerUriString}/{sourceFolderPath}");
var targetUri = new Uri($"{containerUriString}/{targetFolder}");
```

This ensures the URIs are properly formatted for Azure Translation Service to use with managed identity.
