using Azure.Identity;
using Azure.Storage.Blobs;
using DocTranslationV2.Models;
using Microsoft.Extensions.Options;

namespace DocTranslationV2.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly AzureBlobStorageSettings _settings;
    private readonly Lazy<Task> _containerInitialization;

    public BlobStorageService(
        IOptions<TranslationConfiguration> config,
        ILogger<BlobStorageService> logger,
        ICredentialService credentialService)
    {
        _logger = logger;
        _settings = config.Value.AzureBlobStorage;

        // Use cached credential from credential service
        var blobUri = new Uri($"https://{_settings.AccountName}.blob.core.windows.net");
        _blobServiceClient = new BlobServiceClient(blobUri, credentialService.GetBlobStorageCredential());
        _containerClient = _blobServiceClient.GetBlobContainerClient(_settings.ContainerName);

        // Lazy container initialization - only done once
        _containerInitialization = new Lazy<Task>(async () =>
        {
            await _containerClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Container {ContainerName} initialized", _settings.ContainerName);
        });
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure container exists (only executed once across all requests)
            await _containerInitialization.Value;
            
            var blobPath = $"{folderPath}/{fileName}";
            var blobClient = _containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Uploading file {FileName} to blob storage at {BlobPath}", fileName, blobPath);
            
            fileStream.Position = 0;
            await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken);

            _logger.LogInformation("Successfully uploaded file {FileName}", fileName);
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName} to blob storage", fileName);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Attempting to download file from blob storage at path: {BlobPath}", blobPath);

            // Check if the path contains a container name (format: containerName/fileName)
            // This happens when using separate containers per job
            if (!blobPath.Contains('/'))
            {
                // Simple file name, use default container
                _logger.LogInformation("Using default container {Container} for file {FileName}", _settings.ContainerName, blobPath);
                var blobClient = _containerClient.GetBlobClient(blobPath);
                
                // Check if blob exists
                var exists = await blobClient.ExistsAsync(cancellationToken);
                if (!exists.Value)
                {
                    _logger.LogError("Blob {BlobPath} does not exist in container {Container}", blobPath, _settings.ContainerName);
                    throw new FileNotFoundException($"File not found: {blobPath}");
                }
                
                var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;
                _logger.LogInformation("Successfully downloaded {Bytes} bytes from {BlobPath}", memoryStream.Length, blobPath);
                return memoryStream;
            }
            
            // Check if first segment is a container name (starts with "job-")
            var segments = blobPath.Split('/', 2);
            if (segments.Length == 2 && segments[0].StartsWith("job-"))
            {
                // Container-based path: job-{guid}-target/filename.pdf
                var containerName = segments[0];
                var fileName = segments[1];
                
                _logger.LogInformation("Container-based download - Container: {ContainerName}, File: {FileName}", containerName, fileName);
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                
                // Check if container exists
                var containerExists = await containerClient.ExistsAsync(cancellationToken);
                if (!containerExists.Value)
                {
                    _logger.LogError("Container {ContainerName} does not exist", containerName);
                    throw new DirectoryNotFoundException($"Container not found: {containerName}");
                }
                
                var blobClient = containerClient.GetBlobClient(fileName);
                
                // Check if blob exists
                var blobExists = await blobClient.ExistsAsync(cancellationToken);
                if (!blobExists.Value)
                {
                    _logger.LogError("Blob {FileName} does not exist in container {ContainerName}", fileName, containerName);
                    
                    // List all blobs in container to help diagnose
                    _logger.LogInformation("Listing all blobs in container {ContainerName}:", containerName);
                    await foreach (var item in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
                    {
                        _logger.LogInformation("  Found blob: {BlobName}", item.Name);
                    }
                    
                    throw new FileNotFoundException($"File not found in container: {fileName}");
                }
                
                var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;
                _logger.LogInformation("Successfully downloaded {Bytes} bytes from {ContainerName}/{FileName}", 
                    memoryStream.Length, containerName, fileName);
                return memoryStream;
            }
            else
            {
                // Folder-based path in default container: jobs/{jobId}/target/filename.pdf
                _logger.LogInformation("Folder-based download from default container {Container}, path: {BlobPath}", 
                    _settings.ContainerName, blobPath);
                    
                var blobClient = _containerClient.GetBlobClient(blobPath);
                
                // Check if blob exists
                var exists = await blobClient.ExistsAsync(cancellationToken);
                if (!exists.Value)
                {
                    _logger.LogError("Blob {BlobPath} does not exist in container {Container}", blobPath, _settings.ContainerName);
                    throw new FileNotFoundException($"File not found: {blobPath}");
                }
                
                var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;
                _logger.LogInformation("Successfully downloaded {Bytes} bytes from {BlobPath}", memoryStream.Length, blobPath);
                return memoryStream;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from {BlobPath}. Exception type: {ExceptionType}", 
                blobPath, ex.GetType().Name);
            throw;
        }
    }

    public async Task<bool> DeleteFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting folder {FolderPath} from blob storage", folderPath);

            var blobs = _containerClient.GetBlobsAsync(prefix: folderPath, cancellationToken: cancellationToken);
            var deletionTasks = new List<Task>();
            var semaphore = new SemaphoreSlim(10); // Delete 10 blobs concurrently

            await foreach (var blob in blobs)
            {
                await semaphore.WaitAsync(cancellationToken);
                
                var deleteTask = _containerClient.DeleteBlobAsync(blob.Name, cancellationToken: cancellationToken)
                    .ContinueWith(t =>
                    {
                        semaphore.Release();
                        if (t.IsFaulted)
                        {
                            _logger.LogWarning(t.Exception, "Failed to delete blob {BlobName}", blob.Name);
                        }
                    }, cancellationToken);
                
                deletionTasks.Add(deleteTask);
            }

            await Task.WhenAll(deletionTasks);

            _logger.LogInformation("Successfully deleted folder {FolderPath}", folderPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting folder {FolderPath}", folderPath);
            return false;
        }
    }

    public async Task<bool> DeleteContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting container {ContainerName} from blob storage", containerName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            
            // Check if container exists
            var exists = await containerClient.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                _logger.LogWarning("Container {ContainerName} does not exist, nothing to delete", containerName);
                return true; // Not an error - container already gone
            }

            // Delete the entire container
            await containerClient.DeleteAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully deleted container {ContainerName}", containerName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting container {ContainerName}", containerName);
            return false;
        }
    }

    public async Task<List<string>> ListFilesInFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var files = new List<string>();
            var blobs = _containerClient.GetBlobsAsync(prefix: folderPath, cancellationToken: cancellationToken);

            await foreach (var blob in blobs)
            {
                files.Add(blob.Name);
            }

            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files in folder {FolderPath}", folderPath);
            throw;
        }
    }

    public async Task<List<string>> ListFilesInContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing files in container {ContainerName}", containerName);
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            
            // Check if container exists
            var exists = await containerClient.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                _logger.LogWarning("Container {ContainerName} does not exist", containerName);
                return new List<string>();
            }

            var files = new List<string>();
            var blobs = containerClient.GetBlobsAsync(cancellationToken: cancellationToken);

            await foreach (var blob in blobs)
            {
                // Return just the file name, not the full path
                files.Add(blob.Name);
                _logger.LogDebug("Found blob in container {ContainerName}: {BlobName}", containerName, blob.Name);
            }

            _logger.LogInformation("Found {Count} files in container {ContainerName}", files.Count, containerName);
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files in container {ContainerName}", containerName);
            throw;
        }
    }

    public async Task EnsureFolderExistsAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure container exists
            await _containerInitialization.Value;
            
            // Azure Blob Storage requires at least one blob to exist for a folder path
            // Create an empty marker file to establish the folder structure
            // Note: We don't use .placeholder or .keep to avoid cluttering the folder
            // Instead, we create a minimal marker that the Translation Service can overwrite or ignore
            var markerBlobPath = $"{folderPath}/.foldermarker";
            var blobClient = _containerClient.GetBlobClient(markerBlobPath);
            
            // Check if folder already has content
            var hasContent = false;
            await foreach (var blob in _containerClient.GetBlobsAsync(prefix: folderPath, cancellationToken: cancellationToken))
            {
                hasContent = true;
                break;
            }
            
            if (!hasContent)
            {
                _logger.LogInformation("Creating folder structure for {FolderPath}", folderPath);
                
                // Upload minimal empty content to create the folder
                using var emptyStream = new MemoryStream(new byte[0]);
                await blobClient.UploadAsync(emptyStream, overwrite: true, cancellationToken);
                
                _logger.LogInformation("Folder {FolderPath} created successfully", folderPath);
            }
            else
            {
                _logger.LogInformation("Folder {FolderPath} already exists with content", folderPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring folder {FolderPath} exists", folderPath);
            throw;
        }
    }

    public async Task<string> UploadFileToContainerAsync(Stream fileStream, string fileName, string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Uploading file {FileName} to container {ContainerName}", fileName, containerName);
            
            // Get or create the specific container
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var containerExists = await containerClient.ExistsAsync(cancellationToken);
            
            if (!containerExists.Value)
            {
                await containerClient.CreateAsync(publicAccessType: Azure.Storage.Blobs.Models.PublicAccessType.None, cancellationToken: cancellationToken);
                _logger.LogInformation("Created new container {ContainerName}", containerName);
            }
            else
            {
                _logger.LogInformation("Container {ContainerName} already exists", containerName);
            }
            
            // Upload file to container root (no folder path)
            var blobClient = containerClient.GetBlobClient(fileName);

            if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                var blobExists = await blobClient.ExistsAsync(cancellationToken);
                if (blobExists.Value)
                {
                    _logger.LogWarning("Blob {FileName} already exists in container {ContainerName}, will overwrite atomically", fileName, containerName);
                }
            }
            
            // Set content type based on file extension
            var contentType = GetContentType(fileName);
            var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
            {
                HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
                {
                    ContentType = contentType
                }
            };
            
            fileStream.Position = 0;
            var streamLength = fileStream.Length;
            _logger.LogInformation("Uploading {Size} bytes for {FileName}", streamLength, fileName);
            
            await blobClient.UploadAsync(fileStream, uploadOptions, cancellationToken);

            // Verify upload was successful
            var uploadedProperties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            if (uploadedProperties.Value.ContentLength != streamLength)
            {
                _logger.LogError("Upload size mismatch for {FileName}: Expected {Expected} bytes, got {Actual} bytes",
                    fileName, streamLength, uploadedProperties.Value.ContentLength);
                throw new InvalidOperationException($"Upload verification failed: size mismatch for {fileName}");
            }

            _logger.LogInformation("Successfully uploaded file {FileName} to container {ContainerName} with content type {ContentType} ({Size} bytes verified)", 
                fileName, containerName, contentType, uploadedProperties.Value.ContentLength);
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName} to container {ContainerName}", fileName, containerName);
            throw;
        }
    }

    public async Task<Stream> DownloadFileFromContainerAsync(string fileName, string containerName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading file {FileName} from container {ContainerName}", fileName, containerName);
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            
            // Check if container exists
            var containerExists = await containerClient.ExistsAsync(cancellationToken);
            if (!containerExists.Value)
            {
                _logger.LogError("Container {ContainerName} does not exist", containerName);
                throw new DirectoryNotFoundException($"Container not found: {containerName}");
            }
            
            var blobClient = containerClient.GetBlobClient(fileName);
            
            // Check if blob exists
            var blobExists = await blobClient.ExistsAsync(cancellationToken);
            if (!blobExists.Value)
            {
                _logger.LogError("Blob {FileName} does not exist in container {ContainerName}", fileName, containerName);
                throw new FileNotFoundException($"File not found in container: {fileName}");
            }
            
            var memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            
            _logger.LogInformation("Successfully downloaded {Bytes} bytes from container {ContainerName}, file {FileName}", 
                memoryStream.Length, containerName, fileName);
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileName} from container {ContainerName}", fileName, containerName);
            throw;
        }
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".rtf" => "application/rtf",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp" => "application/vnd.oasis.opendocument.presentation",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }
}
