# Azure Resources Setup Guide

This guide will help you set up all the necessary Azure resources for the Document Translation application.

## Prerequisites
- Azure subscription
- Azure CLI installed
- PowerShell or Bash terminal
- Appropriate permissions in Azure AD

## Step 1: Create Resource Group

```bash
# Set variables
$resourceGroup = "rg-document-translation"
$location = "eastus"

# Create resource group
az group create --name $resourceGroup --location $location
```

## Step 2: Create Azure Storage Account

```bash
$storageAccountName = "stdoctranslation$(Get-Random -Maximum 10000)"

# Create storage account
az storage account create `
    --name $storageAccountName `
    --resource-group $resourceGroup `
    --location $location `
    --sku Standard_LRS `
    --kind StorageV2 `
    --access-tier Hot

# Create container
az storage container create `
    --name translations `
    --account-name $storageAccountName `
    --auth-mode login
```

## Step 3: Create Azure Translation Service

```bash
$translationServiceName = "translator-doctranslation"

# Create Translator resource
az cognitiveservices account create `
    --name $translationServiceName `
    --resource-group $resourceGroup `
    --kind TextTranslation `
    --sku S1 `
    --location $location `
    --yes

# Get endpoint and key
$endpoint = az cognitiveservices account show `
    --name $translationServiceName `
    --resource-group $resourceGroup `
    --query properties.endpoint `
    --output tsv

Write-Host "Translation Service Endpoint: $endpoint"
```

## Step 4: Create Azure AD App Registration

### Using Azure Portal:

1. Navigate to Azure Active Directory ? App registrations
2. Click "New registration"
3. Enter a name: "DocTranslation-App"
4. Select "Accounts in this organizational directory only"
5. Click "Register"

**Save the following values:**
- Application (client) ID
- Directory (tenant) ID

### Create Client Secret:

1. In your app registration, go to "Certificates & secrets"
2. Click "New client secret"
3. Add description: "DocTranslation-Secret"
4. Set expiration (recommended: 12 months)
5. Click "Add"
6. **Copy the secret value immediately** (you won't be able to see it again)

### Using Azure CLI:

```bash
# Create app registration
$appName = "DocTranslation-App"
$app = az ad app create --display-name $appName

$appId = ($app | ConvertFrom-Json).appId
Write-Host "App ID: $appId"

# Create service principal
az ad sp create --id $appId

# Create client secret
$secret = az ad app credential reset --id $appId --append
$clientSecret = ($secret | ConvertFrom-Json).password
Write-Host "Client Secret: $clientSecret"

# Get tenant ID
$tenantId = az account show --query tenantId --output tsv
Write-Host "Tenant ID: $tenantId"
```

## Step 5: Assign Role Permissions

### Grant Storage Access to App Registration:

```bash
# Get storage account resource ID
$storageId = az storage account show `
    --name $storageAccountName `
    --resource-group $resourceGroup `
    --query id `
    --output tsv

# Assign Storage Blob Data Contributor role
az role assignment create `
    --role "Storage Blob Data Contributor" `
    --assignee $appId `
    --scope $storageId
```

### Enable Managed Identity for Translation Service:

```bash
# Enable system-assigned managed identity
az cognitiveservices account identity assign `
    --name $translationServiceName `
    --resource-group $resourceGroup

# Get the managed identity principal ID
$principalId = az cognitiveservices account identity show `
    --name $translationServiceName `
    --resource-group $resourceGroup `
    --query principalId `
    --output tsv

# Assign Storage Blob Data Contributor role to managed identity
az role assignment create `
    --role "Storage Blob Data Contributor" `
    --assignee $principalId `
    --scope $storageId
```

## Step 6: Create Application Insights

```bash
$appInsightsName = "appi-doctranslation"

# Create Application Insights
az monitor app-insights component create `
    --app $appInsightsName `
    --location $location `
    --resource-group $resourceGroup `
    --application-type web

# Get connection string
$connectionString = az monitor app-insights component show `
    --app $appInsightsName `
    --resource-group $resourceGroup `
    --query connectionString `
    --output tsv

Write-Host "Application Insights Connection String: $connectionString"
```

## Step 7: Create Azure Key Vault (Optional but Recommended)

```bash
$keyVaultName = "kv-doctrans$(Get-Random -Maximum 10000)"

# Create Key Vault
az keyvault create `
    --name $keyVaultName `
    --resource-group $resourceGroup `
    --location $location

# Grant your app registration access to Key Vault
az keyvault set-policy `
    --name $keyVaultName `
    --object-id $appId `
    --secret-permissions get list

# Store secrets in Key Vault
az keyvault secret set `
    --vault-name $keyVaultName `
    --name "BlobStorageClientSecret" `
    --value $clientSecret

az keyvault secret set `
    --vault-name $keyVaultName `
    --name "ApplicationInsightsConnectionString" `
    --value $connectionString
```

## Step 8: Configure Network Security (Production)

### Storage Account Firewall:

```bash
# Allow access from your IP
$myIp = (Invoke-WebRequest -Uri "https://api.ipify.org").Content

az storage account network-rule add `
    --account-name $storageAccountName `
    --resource-group $resourceGroup `
    --ip-address $myIp

# Enable firewall
az storage account update `
    --name $storageAccountName `
    --resource-group $resourceGroup `
    --default-action Deny
```

### Allow Azure Services:

```bash
az storage account update `
    --name $storageAccountName `
    --resource-group $resourceGroup `
    --bypass AzureServices
```

## Step 9: Summary of Configuration Values

Create a file `azure-config.txt` with the following values:

```
Resource Group: <resource-group-name>
Storage Account Name: <storage-account-name>
Translation Service Endpoint: <translation-endpoint>
Translation Service Region: <region>
App Registration Client ID: <client-id>
Tenant ID: <tenant-id>
Client Secret: <client-secret>
Application Insights Connection String: <connection-string>
Container Name: translations
```

## Step 10: Update Application Configuration

Update your `appsettings.json` or User Secrets with these values:

### Using User Secrets (Development):

```bash
cd DocTranslationV2

dotnet user-secrets set "ApplicationInsights:ConnectionString" "<your-connection-string>"
dotnet user-secrets set "AzureTranslation:Endpoint" "<your-translation-endpoint>"
dotnet user-secrets set "AzureTranslation:Region" "<your-region>"
dotnet user-secrets set "AzureBlobStorage:AccountName" "<your-storage-account>"
dotnet user-secrets set "AzureBlobStorage:TenantId" "<your-tenant-id>"
dotnet user-secrets set "AzureBlobStorage:ClientId" "<your-client-id>"
dotnet user-secrets set "AzureBlobStorage:ClientSecret" "<your-client-secret>"
dotnet user-secrets set "AzureBlobStorage:ContainerName" "translations"
```

### Using Azure App Service Configuration (Production):

```bash
$webAppName = "webapp-doctranslation"

az webapp config appsettings set `
    --name $webAppName `
    --resource-group $resourceGroup `
    --settings `
        ApplicationInsights__ConnectionString="$connectionString" `
        AzureTranslation__Endpoint="$endpoint" `
        AzureTranslation__Region="$location" `
        AzureBlobStorage__AccountName="$storageAccountName" `
        AzureBlobStorage__TenantId="$tenantId" `
        AzureBlobStorage__ClientId="$appId" `
        AzureBlobStorage__ClientSecret="$clientSecret" `
        AzureBlobStorage__ContainerName="translations"
```

## Step 11: Test Configuration

1. Run the application locally:
   ```bash
   dotnet run
   ```

2. Navigate to `https://localhost:5001/Translation`

3. Try uploading a test file

4. Verify in Azure Portal:
   - Storage Account ? Containers ? translations ? Check for uploaded files
   - Application Insights ? Live Metrics ? Check for telemetry
   - Translation Service ? Metrics ? Check for API calls

## Troubleshooting

### Issue: "Storage access denied"
**Solution:** 
- Verify role assignments are complete (can take 5-10 minutes)
- Check if firewall rules are blocking access
- Ensure container "translations" exists

### Issue: "Translation service unauthorized"
**Solution:**
- Verify managed identity is enabled
- Check if managed identity has proper role assignments
- Verify endpoint URL is correct

### Issue: "App registration authentication failed"
**Solution:**
- Verify client ID, tenant ID, and client secret are correct
- Check if client secret has expired
- Ensure app registration has proper API permissions

## Cost Estimation

Approximate monthly costs (based on moderate usage):

- **Storage Account (Standard LRS)**: $0.02/GB
- **Translation Service (S1)**: $10/million characters
- **Application Insights**: First 5GB free, then $2.30/GB
- **Total estimated**: $20-100/month depending on usage

## Cleanup Resources

To delete all resources when done testing:

```bash
az group delete --name $resourceGroup --yes --no-wait
```

## Security Best Practices

1. **Use Key Vault** for storing secrets
2. **Enable Private Endpoints** for storage and translation service
3. **Implement IP restrictions** on storage account
4. **Rotate client secrets** regularly (every 6-12 months)
5. **Enable diagnostic logging** for all services
6. **Use managed identities** wherever possible
7. **Implement least-privilege access** with Azure RBAC
8. **Enable Azure Defender** for storage accounts

## Next Steps

1. Configure continuous deployment
2. Set up monitoring alerts
3. Implement backup and disaster recovery
4. Configure scaling rules
5. Set up staging environments
