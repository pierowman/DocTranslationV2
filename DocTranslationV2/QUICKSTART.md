# Quick Start Guide

Get the Document Translation Application up and running in 15 minutes!

## Prerequisites

? Azure Subscription  
? .NET 9 SDK installed  
? Visual Studio 2022 or VS Code  
? PowerShell or Bash terminal  

## Step 1: Clone and Build (2 minutes)

```bash
# Navigate to project directory
cd DocTranslationV2

# Restore packages
dotnet restore

# Build project
dotnet build
```

## Step 2: Quick Azure Setup (5 minutes)

### Option A: Using Azure Portal

1. **Create Storage Account**
   - Go to Azure Portal ? Storage Accounts ? Create
   - Name: `stdoctrans[unique]`
   - Create container: `translations`

2. **Create Translation Service**
   - Azure AI services ? Translator ? Create
   - Copy the **Endpoint** and **Region**

3. **Create App Registration**
   - Azure AD ? App registrations ? New
   - Create client secret
   - Copy **Client ID**, **Tenant ID**, **Secret**

### Option B: Using PowerShell Script

```powershell
# Run this automated setup script
.\scripts\azure-quick-setup.ps1
```

## Step 3: Configure Application (3 minutes)

### Using User Secrets (Recommended for Development)

```bash
# Initialize user secrets
dotnet user-secrets init

# Add your Azure configuration
dotnet user-secrets set "AzureBlobStorage:AccountName" "YOUR_STORAGE_ACCOUNT"
dotnet user-secrets set "AzureBlobStorage:TenantId" "YOUR_TENANT_ID"
dotnet user-secrets set "AzureBlobStorage:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "AzureBlobStorage:ClientSecret" "YOUR_CLIENT_SECRET"
dotnet user-secrets set "AzureTranslation:Endpoint" "YOUR_TRANSLATION_ENDPOINT"
dotnet user-secrets set "AzureTranslation:Region" "YOUR_REGION"
dotnet user-secrets set "ApplicationInsights:ConnectionString" "YOUR_APP_INSIGHTS_CONNECTION"
```

### Or Update appsettings.json

```json
{
  "AzureBlobStorage": {
    "AccountName": "YOUR_STORAGE_ACCOUNT",
    "TenantId": "YOUR_TENANT_ID",
    "ClientId": "YOUR_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "ContainerName": "translations"
  },
  "AzureTranslation": {
    "Endpoint": "https://YOUR_ENDPOINT.cognitiveservices.azure.com/",
    "Region": "YOUR_REGION"
  },
  "ApplicationInsights": {
    "ConnectionString": "YOUR_CONNECTION_STRING"
  }
}
```

## Step 4: Set Permissions (3 minutes)

```bash
# Assign Storage Blob Data Contributor role to App Registration
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee YOUR_CLIENT_ID \
    --scope /subscriptions/YOUR_SUBSCRIPTION/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE

# Enable managed identity for Translation Service
az cognitiveservices account identity assign \
    --name YOUR_TRANSLATION_SERVICE \
    --resource-group YOUR_RG

# Assign Storage access to managed identity
az role assignment create \
    --role "Storage Blob Data Contributor" \
    --assignee MANAGED_IDENTITY_PRINCIPAL_ID \
    --scope /subscriptions/YOUR_SUBSCRIPTION/resourceGroups/YOUR_RG/providers/Microsoft.Storage/storageAccounts/YOUR_STORAGE
```

## Step 5: Run Application (2 minutes)

```bash
# Start the application
dotnet run

# Or in Visual Studio
# Press F5 or click "Debug" ? "Start Debugging"
```

The application will open at: `https://localhost:5001/Translation`

## Step 6: Test Translation (5 minutes)

### Quick Test Scenario

1. **Prepare Test File**
   - Create a simple text file: `test.txt`
   - Add some English text

2. **Upload and Translate**
   - Click "Select Documents"
   - Choose your test file
   - Source: "Auto-detect" (checked)
   - Target: Select "Spanish"
   - Mode: "Sync Processing"
   - Click "Start Translation"

3. **Download Result**
   - Wait for completion (should be quick for small files)
   - Click "Download" on the translated file
   - Open and verify translation

4. **Cleanup**
   - Click "Delete Temporary Files"
   - Confirm deletion

## Common Quick Start Issues

### Issue: "Storage access denied"

**Solution:**
```bash
# Wait 5 minutes for role assignment to propagate
# Then verify assignment:
az role assignment list \
    --assignee YOUR_CLIENT_ID \
    --scope YOUR_STORAGE_SCOPE
```

### Issue: "Translation endpoint not found"

**Solution:**
- Verify endpoint URL format: `https://[name].cognitiveservices.azure.com/`
- Check region matches your translation service region
- Ensure managed identity is enabled

### Issue: "Application Insights not receiving data"

**Solution:**
- Verify connection string format
- Check if AI resource exists
- Data may take 1-2 minutes to appear

### Issue: "Build errors"

**Solution:**
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

## Verification Checklist

Before proceeding, verify:

- ? Application builds without errors
- ? Application runs and opens in browser
- ? Translation page loads
- ? File upload shows file list
- ? Test translation completes successfully
- ? Translated file downloads
- ? Azure Storage shows uploaded files
- ? Application Insights shows telemetry

## Next Steps

Now that you're up and running:

1. **Read Full Documentation**
   - [README.md](README.md) - Comprehensive guide
   - [AZURE_SETUP.md](AZURE_SETUP.md) - Detailed Azure configuration
   - [TESTING_GUIDE.md](TESTING_GUIDE.md) - Testing scenarios

2. **Try Advanced Features**
   - Upload Word document with images
   - Test multi-language translation
   - Upload multiple files
   - Try async processing

3. **Configure for Production**
   - Set up Key Vault for secrets
   - Configure network security
   - Enable monitoring alerts
   - Set up continuous deployment

4. **Customize Application**
   - Modify supported file types
   - Adjust file size limits
   - Customize UI theme
   - Add custom translation glossaries

## Development Workflow

```bash
# Make changes to code
# ...

# Run locally
dotnet run

# Test changes
# ...

# Build for release
dotnet publish -c Release

# Deploy to Azure
# See deployment guide
```

## Troubleshooting Commands

```bash
# Check Azure resources
az resource list --resource-group YOUR_RG

# Test storage connection
az storage blob list \
    --account-name YOUR_STORAGE \
    --container-name translations \
    --auth-mode login

# Check translation service
az cognitiveservices account show \
    --name YOUR_TRANSLATION_SERVICE \
    --resource-group YOUR_RG

# View application logs
dotnet run --verbosity detailed
```

## Quick Reference

### Supported File Formats
- Documents: PDF, DOCX, DOC, RTF, TXT, ODT
- Presentations: PPTX, PPT, ODP
- Spreadsheets: XLSX, XLS, ODS
- Web: HTML, HTM, XML

### File Size Limits
- Sync Processing: 50 MB
- Async Processing: 500 MB
- Total Upload: 500 MB per request

### Processing Modes
- **Sync**: Single file, immediate results, < 50 MB
- **Async**: Multiple files, large files, polling for results

### Useful URLs
- Application: `https://localhost:5001/Translation`
- Azure Portal: `https://portal.azure.com`
- App Insights: `https://portal.azure.com ? Application Insights ? YOUR_RESOURCE`

## Getting Help

- Check [README.md](README.md) for detailed documentation
- Review [TESTING_GUIDE.md](TESTING_GUIDE.md) for test scenarios
- Check Application Insights logs for errors
- Review Azure resource configurations

## Clean Up (If Testing Only)

To remove all Azure resources:

```bash
# Delete resource group (removes all resources)
az group delete --name YOUR_RG --yes --no-wait

# Or delete individual resources
az storage account delete --name YOUR_STORAGE --yes
az cognitiveservices account delete --name YOUR_TRANSLATION --resource-group YOUR_RG
az monitor app-insights component delete --app YOUR_APP_INSIGHTS --resource-group YOUR_RG
```

## Success Criteria

You've successfully completed the quick start when:

? Application runs without errors  
? Test file uploads successfully  
? Translation completes  
? Translated file downloads  
? Cleanup works correctly  
? Azure resources are configured  
? Application Insights receives data  

**Congratulations! You're ready to use the Document Translation Application!**

---

**Estimated Total Time: 15-20 minutes**

Need more help? Check the full documentation or review the troubleshooting section.
