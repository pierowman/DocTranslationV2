# Azure Document Translation Service Application

This ASP.NET Core application provides a comprehensive solution for translating documents using Azure Document Translation Service with integrated blob storage and image handling capabilities.

## Features

### Core Translation Features
- **Multi-file Upload**: Upload multiple documents for translation
- **Language Support**: Dynamically fetches supported languages from Azure Translation Service
- **Auto Language Detection**: Automatically identifies source language
- **Async & Sync Processing**: 
  - Async mode for bulk files and long-running translations
  - Sync mode for single file immediate results
- **Long-running Support**: Handles translations exceeding 5 minutes with proper status polling

### Image Handling
- **Image Detection**: Automatically detects images in Word documents and PDFs
- **Image Extraction**: Extracts all images from documents
- **Separate Image Translation**: Creates separate PDF of extracted images for translation
- **Image Re-integration**: Replaces translated images back into translated documents

### Storage & Authentication
- **Azure Blob Storage**: Uses EntraID App Registration for blob storage connections
- **Managed Identities**: Translation service uses managed identities for blob access
- **Temporary Folders**: Creates unique source/target folders per translation job
- **Auto-cleanup**: Deletes temporary folders after download

### File Support
Supported file formats:
- PDF (.pdf)
- Word Documents (.docx)
- PowerPoint (.pptx)
- Excel (.xlsx)
- Text files (.txt)
- HTML files (.html, .htm)
- Rich Text Format (.rtf)
- OpenDocument formats (.odt, .ods, .odp)

### Monitoring & Logging
- **Application Insights Integration**: Comprehensive logging and monitoring
- **Status Tracking**: Real-time translation status updates
- **Error Handling**: Detailed error messages and logging

## Prerequisites

1. **Azure Resources Required:**
   - Azure Document Translation Service
   - Azure Blob Storage Account
   - Azure AD App Registration
   - Application Insights (optional but recommended)

2. **.NET SDK:**
   - .NET 9.0 or later

## Configuration

### 1. Azure AD App Registration

Create an App Registration in Azure AD:

1. Go to Azure Portal ? Azure Active Directory ? App registrations
2. Click "New registration"
3. Note the following values:
   - Application (client) ID
   - Directory (tenant) ID
4. Create a client secret under "Certificates & secrets"
5. Grant permissions to Azure Blob Storage

### 2. Blob Storage Setup

1. Create an Azure Storage Account
2. Create a container named `translations` (or customize in settings)
3. Assign the following roles to your App Registration:
   - Storage Blob Data Contributor
4. Configure managed identity for Azure Translation Service to access blob storage

### 3. Translation Service Setup

1. Create an Azure Document Translation Service resource
2. Note the endpoint URL and region
3. Configure managed identity for the translation service
4. Grant the managed identity access to your blob storage

### 4. Application Settings

Update `appsettings.json` with your Azure resource details:

```json
{
  "ApplicationInsights": {
    "ConnectionString": "YOUR_APPLICATION_INSIGHTS_CONNECTION_STRING"
  },
  "AzureTranslation": {
    "Endpoint": "https://YOUR_TRANSLATOR_RESOURCE.cognitiveservices.azure.com/",
    "Region": "YOUR_REGION"
  },
  "AzureBlobStorage": {
    "AccountName": "YOUR_STORAGE_ACCOUNT_NAME",
    "TenantId": "YOUR_TENANT_ID",
    "ClientId": "YOUR_APP_REGISTRATION_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "ContainerName": "translations"
  }
}
```

**Security Note:** In production, store sensitive values (ClientSecret, connection strings) in:
- Azure Key Vault
- User Secrets (for development)
- Environment Variables
- Azure App Configuration

### 5. User Secrets (Development)

For local development, use User Secrets:

```bash
dotnet user-secrets init
dotnet user-secrets set "AzureBlobStorage:ClientSecret" "YOUR_SECRET"
dotnet user-secrets set "ApplicationInsights:ConnectionString" "YOUR_CONNECTION_STRING"
```

## Installation

1. **Clone or download the project**

2. **Restore NuGet packages:**
   ```bash
   dotnet restore
   ```

3. **Configure settings** (see Configuration section above)

4. **Run the application:**
   ```bash
   dotnet run
   ```

5. **Access the application:**
   - Navigate to `https://localhost:5001` (or configured port)

## Usage

### Basic Translation Workflow

1. **Upload Files:**
   - Click "Select Documents" and choose one or more files
   - View selected files with size information

2. **Configure Languages:**
   - Source Language: Auto-detect or select manually
   - Target Languages: Select one or more target languages

3. **Choose Processing Mode:**
   - **Async**: Recommended for multiple files or large documents
   - **Sync**: For single small files requiring immediate results

4. **Start Translation:**
   - Click "Start Translation"
   - Monitor progress in real-time

5. **Download Results:**
   - View all translated files organized by language
   - Download individual files or all at once
   - Clean up temporary files after download

### Advanced Features

#### Image-containing Documents

When uploading Word documents or PDFs with images:

1. The system automatically detects images
2. Creates a separate PDF of all images
3. Translates both text and images
4. Re-integrates translated images into final document

#### Long-running Translations

For translations exceeding 5 minutes:

- The system uses async processing automatically
- Status polling continues until completion
- Progress updates every 5 seconds
- No timeout limitations

#### Bulk Processing

For multiple files:

- Upload all files at once
- System automatically uses async mode
- Each file is processed independently
- Results organized by target language

## Project Structure

```
DocTranslationV2/
??? Controllers/
?   ??? HomeController.cs
?   ??? TranslationController.cs
??? Models/
?   ??? ErrorViewModel.cs
?   ??? ImageModels.cs
?   ??? TranslationConfiguration.cs
?   ??? TranslationModels.cs
??? Services/
?   ??? BlobStorageService.cs
?   ??? DocumentTranslationService.cs
?   ??? ImageExtractionService.cs
?   ??? IServices.cs
??? Views/
?   ??? Home/
?   ??? Shared/
?   ??? Translation/
?       ??? Index.cshtml
??? wwwroot/
??? appsettings.json
??? Program.cs
```

## Key Components

### Services

1. **IBlobStorageService**: Manages Azure Blob Storage operations
   - Upload files
   - Download files
   - Delete folders
   - Generate SAS URLs

2. **IDocumentTranslationService**: Handles translation operations
   - Get supported languages
   - Start translations (sync/async)
   - Check translation status
   - Validate file types

3. **IImageExtractionService**: Processes images in documents
   - Extract images from PDFs
   - Extract images from Word documents
   - Create PDF from images
   - Replace images in translated documents

### Controllers

1. **TranslationController**: Main API endpoints
   - `/Translation/Index` - Main UI
   - `/Translation/Translate` - Start translation
   - `/Translation/GetStatus` - Check job status
   - `/Translation/DownloadFile` - Download translated file
   - `/Translation/CleanupJob` - Delete temporary files
   - `/Translation/GetTranslatedFiles` - List results

## Error Handling

The application includes comprehensive error handling:

- File validation before upload
- Translation status monitoring
- Detailed error messages in UI
- Application Insights logging for debugging

## Performance Considerations

- **File Size Limits**: Configured for up to 500MB
- **Concurrent Uploads**: Supports multiple file upload
- **Status Polling**: 5-second intervals to minimize API calls
- **Cleanup**: Automatic temporary file management

## Security Best Practices

1. **Authentication**: Uses EntraID App Registration
2. **Managed Identity**: Translation service to blob access
3. **Secrets Management**: Store in Key Vault
4. **Network Security**: Configure firewall rules
5. **HTTPS**: Enforce SSL/TLS

## Troubleshooting

### Common Issues

1. **"Translation service connection failed"**
   - Verify endpoint URL and region
   - Check managed identity permissions
   - Ensure service is properly provisioned

2. **"Blob storage access denied"**
   - Verify App Registration credentials
   - Check role assignments on storage account
   - Ensure container exists

3. **"File upload failed"**
   - Check file size limits
   - Verify file format is supported
   - Review IIS/Kestrel upload limits

4. **"Image extraction failed"**
   - Ensure document is not corrupted
   - Check if PDF is image-only (not supported)
   - Verify iText7 license for production

## Monitoring

With Application Insights enabled, monitor:

- Translation request rates
- Success/failure rates
- Processing durations
- Error traces
- Custom events

## Production Deployment

### Azure App Service

1. Publish the application:
   ```bash
   dotnet publish -c Release
   ```

2. Deploy to Azure App Service

3. Configure application settings in Azure Portal

4. Enable managed identity for App Service

5. Grant App Service identity access to resources

### Docker

The project includes Docker support:

```bash
docker build -t doctranslation .
docker run -p 8080:80 doctranslation
```

## License

This project uses several third-party packages:
- iText7: Check licensing requirements for production use
- Azure SDK: MIT License
- DocumentFormat.OpenXml: MIT License

## Support

For issues or questions:
- Check Azure service health
- Review Application Insights logs
- Verify configuration settings
- Check Azure role assignments

## Future Enhancements

Potential improvements:
- Support for more document formats
- Custom translation glossaries
- Batch job history and tracking
- User authentication and multi-tenancy
- Progress notifications via SignalR
- OCR for image-only PDFs
- Custom translation memory
