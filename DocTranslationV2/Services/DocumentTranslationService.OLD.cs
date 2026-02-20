using Azure;
using Azure.AI.Translation.Document;
using DocTranslationV2.Models;
using DocTranslationV2.Constants;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace DocTranslationV2.Services;

public class DocumentTranslationService : IDocumentTranslationService
{
    private readonly DocumentTranslationClient _batchClient;
    private readonly SingleDocumentTranslationClient _singleDocClient;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IImageExtractionService _imageExtractionService;
    private readonly IImageReplacementService _imageReplacementService;
    private readonly ILanguageService _languageService;
    private readonly ILogger<DocumentTranslationService> _logger;
    private readonly ICredentialService _credentialService;
    private readonly AzureTranslationSettings _settings;
    private readonly AzureBlobStorageSettings _blobSettings;
    
    // Thread-safe concurrent dictionaries replace regular dictionaries with locks
    private readonly ConcurrentDictionary<string, DocumentTranslationOperation> _activeOperations = new();
    private readonly ConcurrentDictionary<string, (JobStatus Status, DateTime CachedAt)> _terminalJobsCache = new();
    private readonly ConcurrentDictionary<string, JobMetadata> _jobMetadata = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);
    
    // Cache for language code to name mapping
    private readonly ConcurrentDictionary<string, string> _languageNameCache = new();
    private DateTime _languageCacheExpiration = DateTime.MinValue;
    private readonly object _languageCacheLock = new();

    // Job metadata class to track additional job information
    private class JobMetadata
    {
        public string JobId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public List<string> AllOperationIds { get; set; } = new();
        public bool HasImageProcessing { get; set; }
        public string SourceContainerName { get; set; } = string.Empty;
        public string TargetContainerName { get; set; } = string.Empty;
        public Dictionary<string, string> TargetContainersByLanguage { get; set; } = new();
        public Dictionary<string, string> OperationIdToLanguage { get; set; } = new();
        public List<IFormFile> OriginalFiles { get; set; } = new();
        public List<string> TargetLanguages { get; set; } = new();
        public string CurrentPhase { get; set; } = "Initializing";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastPhaseUpdate { get; set; } = DateTime.UtcNow;
    }

    public DocumentTranslationService(
        IOptions<TranslationConfiguration> config,
        IBlobStorageService blobStorageService,
        IImageExtractionService imageExtractionService,
        IImageReplacementService imageReplacementService,
        ILanguageService languageService,
        ILogger<DocumentTranslationService> logger,
        ICredentialService credentialService)
    {
        _settings = config.Value.AzureTranslation;
        _blobSettings = config.Value.AzureBlobStorage;
        _blobStorageService = blobStorageService;
        _imageExtractionService = imageExtractionService;
        _imageReplacementService = imageReplacementService;
        _languageService = languageService;
        _logger = logger;
        _credentialService = credentialService;

        var credential = credentialService.GetTranslationServiceCredential();
        
        _batchClient = new DocumentTranslationClient(new Uri(_settings.Endpoint), credential);
        _singleDocClient = new SingleDocumentTranslationClient(new Uri(_settings.Endpoint), credential);
    }

    public bool IsFileSupported(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return _settings.SupportedFileTypes.Batch.Contains(extension) || 
               _settings.SupportedFileTypes.Sync.Contains(extension);
    }

    public bool IsFileSupportedForMode(string fileName, bool isAsync)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var supportedExtensions = isAsync ? _settings.SupportedFileTypes.Batch : _settings.SupportedFileTypes.Sync;
        return supportedExtensions.Contains(extension);
    }

    public bool SupportsImageProcessing(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return _settings.SupportedFileTypes.ImageProcessingSupported.Contains(extension);
    }

    public async Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching supported languages from Azure Translation Service");
            var languages = await _languageService.GetSupportedLanguagesAsync(cancellationToken);
            _logger.LogInformation("Retrieved {Count} supported languages", languages.Count);
            return languages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching supported languages");
            return await _languageService.GetSupportedLanguagesAsync(cancellationToken);
        }
    }

    public async Task<TranslationResponse> TranslateDocumentsAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        var response = new TranslationResponse { IsAsync = request.UseAsyncProcessing };
        var jobId = Guid.NewGuid().ToString();

        try
        {
            _logger.LogInformation("Starting translation job {JobId} with {FileCount} files", jobId, request.Files.Count);

            var mode = request.UseAsyncProcessing ? "async (batch)" : "sync";
            foreach (var file in request.Files)
            {
                if (!IsFileSupportedForMode(file.FileName, request.UseAsyncProcessing))
                {
                    var supportedExtensions = request.UseAsyncProcessing 
                        ? string.Join(", ", _settings.SupportedFileTypes.Batch)
                        : string.Join(", ", _settings.SupportedFileTypes.Sync);
                        
                    throw new InvalidOperationException(
                        $"File '{file.FileName}' is not supported for {mode} translation. Supported formats: {supportedExtensions}");
                }
            }

            if (request.ProcessImages && !request.UseAsyncProcessing)
            {
                _logger.LogWarning("Image processing requested for sync mode - disabling as it's only supported in async mode");
                request.ProcessImages = false;
            }

            if (request.ProcessImages)
            {
                var unsupportedFiles = request.Files.Where(f => !SupportsImageProcessing(f.FileName)).Select(f => f.FileName).ToList();
                if (unsupportedFiles.Any())
                {
                    _logger.LogInformation("Image processing enabled but {Count} file(s) don't support it: {Files}",
                        unsupportedFiles.Count, string.Join(", ", unsupportedFiles));
                }
            }

            if (request.Files.Count > 1 && !request.UseAsyncProcessing)
            {
                _logger.LogWarning("Multiple files detected, forcing async processing");
                request.UseAsyncProcessing = true;
                response.IsAsync = true;
            }

            if (request.UseAsyncProcessing)
            {
                await ProcessBatchTranslationAsync(request, jobId, response, cancellationToken);
            }
            else
            {
                await ProcessSynchronousTranslationAsync(request, jobId, response, cancellationToken);
            }

            _logger.LogInformation("Translation job {JobId} started successfully", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting translation job {JobId}", jobId);
            response.Status = "Failed";
            response.ErrorMessage = ex.Message;
        }

        return response;
    }

    private async Task ProcessBatchTranslationAsync(TranslationRequest request, string jobId, TranslationResponse response, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting BATCH translation for {FileCount} files to {LanguageCount} language(s)", 
            request.Files.Count, request.TargetLanguages.Count);

        // Use separate containers per job
        var sourceContainerName = ContainerNamePatterns.GetSourceContainerName(jobId);

        // Initialize metadata for tracking job phases using thread-safe AddOrUpdate
        var metadata = _jobMetadata.AddOrUpdate(
            jobId,
            // Add factory
            new JobMetadata
            {
                JobId = jobId,
                CurrentPhase = request.ProcessImages ? JobPhases.UploadingFiles : JobPhases.Initializing,
                SourceContainerName = sourceContainerName,
                OriginalFiles = request.Files,
                TargetLanguages = request.TargetLanguages,
                HasImageProcessing = request.ProcessImages
            },
            // Update factory (shouldn't happen for new jobs, but ensures thread safety)
            (key, existing) =>
            {
                existing.CurrentPhase = request.ProcessImages ? JobPhases.UploadingFiles : JobPhases.Initializing;
                existing.SourceContainerName = sourceContainerName;
                existing.OriginalFiles = request.Files;
                existing.TargetLanguages = request.TargetLanguages;
                existing.HasImageProcessing = request.ProcessImages;
                return existing;
            });

        // IMPORTANT: Check for existing source container and clean it up if needed
        await CleanupExistingContainersIfNeededAsync(sourceContainerName, sourceContainerName, cancellationToken);

        // Upload files to source container (includes image extraction if enabled)
        await ProcessAndUploadFilesForBatchAsync(request.Files, sourceContainerName, request.ProcessImages, request.ImageFiltering, jobId, cancellationToken);

        // Update phase to starting translation
        UpdateJobPhase(jobId, JobPhases.StartingTranslation);

        // Start translations for each target language with separate target containers
        // This allows us to clearly identify which files belong to which language
        var operationIds = new List<string>();
        
        foreach (var targetLanguage in request.TargetLanguages)
        {
            var targetContainerName = ContainerNamePatterns.GetTargetContainerName(jobId, targetLanguage);
            _logger.LogInformation("Starting translation to {Language} with target container {Container}", 
                targetLanguage, targetContainerName);
            
            var operationId = await StartBatchTranslationWithoutWaitingAsync(
                sourceContainerName, 
                targetContainerName, 
                request.SourceLanguage,
                new List<string> { targetLanguage },
                request.AutoDetectLanguage, 
                jobId, 
                request.ProcessImages, 
                request.Files, 
                targetLanguage,
                cancellationToken);
            
            operationIds.Add(operationId);
        }
        
        // Update phase to translating after operations are started
        UpdateJobPhase(jobId, JobPhases.TranslatingDocuments);
        
        // Start ONE background task to monitor ALL operations for this job
        if (request.ProcessImages)
        {
            _logger.LogInformation("Starting background monitoring for job {JobId} with {OperationCount} operations", 
                jobId, operationIds.Count);
            _ = Task.Run(async () => await MonitorAllTranslationsAndProcessImagesAsync(jobId, CancellationToken.None));
        }
        
        response.JobId = jobId;
        response.Status = TranslationStatus.InProgress;
        
        // Set the current phase in the response
        if (_jobMetadata.TryGetValue(jobId, out var currentMetadata))
        {
            response.CurrentPhase = currentMetadata.CurrentPhase;
            _logger.LogInformation("Returning response with CurrentPhase: {CurrentPhase} for job {JobId}", 
                currentMetadata.CurrentPhase, jobId);
        }
        
        _logger.LogInformation("Translation operations {OperationIds} started for job {JobId} with {LanguageCount} language(s)", 
            string.Join(", ", operationIds), jobId, request.TargetLanguages.Count);
        
        _logger.LogInformation("Translation job {JobId} queued successfully for {LanguageCount} language(s)", 
            jobId, request.TargetLanguages.Count);
    }

    private async Task ProcessSynchronousTranslationAsync(TranslationRequest request, string jobId, TranslationResponse response, CancellationToken cancellationToken)
    {
        if (request.Files.Count != 1)
            throw new InvalidOperationException("Synchronous translation requires exactly one file");

        var file = request.Files[0];
        var fileName = file.FileName;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            foreach (var targetLang in request.TargetLanguages)
            {
                _logger.LogInformation("Translating {FileName} to {TargetLanguage}", fileName, targetLang);

                using var fileStream = file.OpenReadStream();
                var fileData = new MultipartFormFileData(fileName, fileStream, GetContentType(extension));
                var documentContent = new DocumentTranslateContent(fileData);

                var translationResult = await _singleDocClient.TranslateAsync(targetLang, documentContent,
                    sourceLanguage: request.AutoDetectLanguage ? null : request.SourceLanguage,
                    cancellationToken: cancellationToken);

                var targetFolderPath = $"jobs/{jobId}/target/{targetLang}";
                
                // Ensure proper disposal of result stream
                using (var resultStream = translationResult.Value.ToStream())
                {
                    await _blobStorageService.UploadFileAsync(resultStream, fileName, targetFolderPath, cancellationToken);
                }

                var languageName = await GetLanguageNameAsync(targetLang, cancellationToken);
                
                response.TranslatedFiles.Add(new TranslatedFile
                {
                    OriginalFileName = fileName,
                    TargetLanguage = targetLang,
                    TargetLanguageName = languageName,
                    TranslatedBlobUrl = $"{targetFolderPath}/{fileName}"
                });
            }

            response.JobId = jobId;
            response.Status = TranslationStatus.Succeeded;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure translation failed for {FileName}: Status={Status}, ErrorCode={ErrorCode}",
                fileName, ex.Status, ex.ErrorCode);
            response.Status = TranslationStatus.Failed;
            response.ErrorMessage = $"Translation failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in synchronous translation for {FileName}", fileName);
            response.Status = TranslationStatus.Failed;
            response.ErrorMessage = ex.Message;
        }
    }

    private string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
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
            _ => "application/octet-stream"
        };
    }

    private async Task<List<string>> ProcessAndUploadFilesAsync(List<IFormFile> files, string sourceFolderPath, bool processImages, CancellationToken cancellationToken)
    {
        var processedFiles = new List<string>();
        var semaphore = new SemaphoreSlim(4);
        var processingTasks = new List<Task<string>>();

        foreach (var file in files)
        {
            var fileName = file.FileName;
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    // Upload the file first
                    using (var fileStream = file.OpenReadStream())
                    {
                        await _blobStorageService.UploadFileAsync(fileStream, fileName, sourceFolderPath, cancellationToken);
                    }

                    return fileName;
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            processingTasks.Add(task);
        }

        processedFiles.AddRange(await Task.WhenAll(processingTasks));
        return processedFiles;
    }

    private async Task<List<string>> ProcessAndUploadFilesForBatchAsync(List<IFormFile> files, string containerName, bool processImages, ImageFilteringOptions? filteringOptions, string jobId, CancellationToken cancellationToken)
    {
        var processedFiles = new List<string>();
        var semaphore = new SemaphoreSlim(4);
        var processingTasks = new List<Task<string>>();
        
        // Track when we start image extraction to update phase
        var imageExtractionStarted = false;
        var imageExtractionLock = new object();

        foreach (var file in files)
        {
            var fileName = file.FileName;
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    // Upload the file first
                    using (var fileStream = file.OpenReadStream())
                    {
                        await _blobStorageService.UploadFileToContainerAsync(fileStream, fileName, containerName, cancellationToken);
                    }
                    _logger.LogInformation("Uploaded {FileName} to container {Container}", fileName, containerName);
                    
                    // Process images if requested and supported
                    if (processImages && (extension == ".pdf" || extension == ".docx"))
                    {
                        // Update phase to "Extracting Images" when we start the first image extraction
                        lock (imageExtractionLock)
                        {
                            if (!imageExtractionStarted)
                            {
                                imageExtractionStarted = true;
                                UpdateJobPhase(jobId, JobPhases.ExtractingImages);
                                _logger.LogInformation("Starting image extraction phase for job {JobId}", jobId);
                            }
                        }
                        
                        await ProcessImageExtractionAsync(file, fileName, containerName, extension, filteringOptions, jobId, cancellationToken);
                    }
                    
                    return fileName;
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            processingTasks.Add(task);
        }

        processedFiles.AddRange(await Task.WhenAll(processingTasks));
        
        if (processImages)
        {
            _logger.LogInformation("File upload and image extraction completed for job {JobId}", jobId);
        }
        else
        {
            _logger.LogInformation("File upload completed for job {JobId}", jobId);
        }
        
        return processedFiles;
    }

    /// <summary>
    /// Extracts images from a document file for separate translation.
    /// This is called after the file has been uploaded to blob storage.
    /// </summary>
    private async Task ProcessImageExtractionAsync(IFormFile file, string fileName, string containerName, string extension, ImageFilteringOptions? filteringOptions, string jobId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Extracting images from {FileName}", fileName);
            
            // Download the file we just uploaded to process it
            using var downloadStream = await _blobStorageService.DownloadFileFromContainerAsync(fileName, containerName, cancellationToken);
            
            // Extract images based on file type with filtering options
            var imageInfo = extension == ".pdf"
                ? await _imageExtractionService.ExtractImagesFromPdfAsync(downloadStream, fileName, filteringOptions)
                : await _imageExtractionService.ExtractImagesFromWordAsync(downloadStream, fileName, filteringOptions);

            // Only create images PDF if document has both images and text content
            if (imageInfo.HasImages && imageInfo.HasTextContent)
            {
                _logger.LogInformation("Creating images PDF for {FileName} with {ImageCount} images", fileName, imageInfo.Images.Count);
                
                // Create a PDF containing all extracted images
                using var imagesPdfStream = await _imageExtractionService.CreatePdfFromImagesAsync(imageInfo.Images, jobId);
                var imagesPdfName = FileNamePatterns.GetImagesPdfFileName(fileName);
                await _blobStorageService.UploadFileToContainerAsync(imagesPdfStream, imagesPdfName, containerName, cancellationToken);

                // Save metadata in a separate metadata container
                var metadataContainerName = $"{containerName}-metadata";
                var metadataFileName = FileNamePatterns.GetImageMetadataFileName(fileName);
                var metadataJson = System.Text.Json.JsonSerializer.Serialize(imageInfo.Images);
                using var metadataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(metadataJson));
                await _blobStorageService.UploadFileToContainerAsync(metadataStream, metadataFileName, metadataContainerName, cancellationToken);
                
                _logger.LogInformation("Successfully uploaded images PDF and metadata for {FileName}", fileName);
            }
            else if (!imageInfo.HasTextContent)
            {
                _logger.LogInformation("Document {FileName} has no text content, skipping image extraction", fileName);
            }
            else
            {
                _logger.LogInformation("Document {FileName} has no images to extract", fileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing images for {FileName}, continuing with text-only translation", fileName);
            // Continue with translation even if image processing fails
        }
    }

    private async Task ProcessSingleFileForBatchAsync(IFormFile file, string fileName, string sourceFolderPath, string extension, bool processImages, string jobId, CancellationToken cancellationToken)
    {
        if (processImages && (extension == ".docx" || extension == ".pdf"))
        {
            await ProcessDocumentWithImages(file, fileName, sourceFolderPath, extension, processImages, jobId, cancellationToken);
        }
        else
        {
            using var stream = file.OpenReadStream();
            await _blobStorageService.UploadFileAsync(stream, fileName, sourceFolderPath, cancellationToken);
        }
    }

    private async Task ProcessDocumentWithImages(IFormFile file, string fileName, string folderPath, string extension, bool processImages, string jobId, CancellationToken cancellationToken)
    {
        using var fileStream = file.OpenReadStream();
        await _blobStorageService.UploadFileAsync(fileStream, fileName, folderPath, cancellationToken);

        if (processImages)
        {
            try
            {
                var blobPath = $"{folderPath}/{fileName}";
                using var downloadStream = await _blobStorageService.DownloadFileAsync(blobPath, cancellationToken);

                var imageInfo = extension == ".pdf"
                    ? await _imageExtractionService.ExtractImagesFromPdfAsync(downloadStream, fileName)
                    : await _imageExtractionService.ExtractImagesFromWordAsync(downloadStream, fileName);

                if (imageInfo.HasImages && imageInfo.HasTextContent)
                {
                    using var imagesPdfStream = await _imageExtractionService.CreatePdfFromImagesAsync(imageInfo.Images, jobId);
                    var imagesPdfName = $"{Path.GetFileNameWithoutExtension(fileName)}_images.pdf";
                    await _blobStorageService.UploadFileAsync(imagesPdfStream, imagesPdfName, folderPath, cancellationToken);

                    var metadataFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_image_metadata.json";
                    var metadataJson = System.Text.Json.JsonSerializer.Serialize(imageInfo.Images);
                    using var metadataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(metadataJson));
                    await _blobStorageService.UploadFileAsync(metadataStream, metadataFileName, folderPath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing images for {FileName}, continuing with text-only translation", fileName);
            }
        }
    }

    private async Task<string> StartBatchTranslationAsync(string sourceContainerName, string targetContainerName, string? sourceLanguage,
        List<string> targetLanguages, bool autoDetect, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting batch translation - Source container: {SourceContainer}, Target container: {TargetContainer}", 
                sourceContainerName, targetContainerName);

            var blobUri = new Uri($"https://{_blobSettings.AccountName}.blob.core.windows.net");
            var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(blobUri, _credentialService.GetBlobStorageCredential());

            var sourceContainerClient = blobServiceClient.GetBlobContainerClient(sourceContainerName);
            var targetContainerClient = blobServiceClient.GetBlobContainerClient(targetContainerName);
            
            var sourceExists = await sourceContainerClient.ExistsAsync(cancellationToken);
            if (!sourceExists.Value)
            {
                throw new InvalidOperationException($"Source container {sourceContainerName} should exist after file upload but doesn't!");
            }
            
            await CreateContainerWithRetryAsync(targetContainerClient, targetContainerName, cancellationToken);
            _logger.LogInformation("Target container {Container} ready", targetContainerName);

            var sourceUri = sourceContainerClient.Uri;
            var targetUri = targetContainerClient.Uri;

            _logger.LogInformation("Translation URIs - Source: {SourceUri}, Target: {TargetUri}", sourceUri, targetUri);
            _logger.LogInformation("IMPORTANT: Translation Service must have 'Storage Blob Data Contributor' role on storage account '{StorageAccount}'", 
                _blobSettings.AccountName);

            var targetLang = targetLanguages.First();
            _logger.LogInformation("Starting translation for language: {TargetLanguage}", targetLang);

            _logger.LogInformation("Waiting 2 seconds for blob storage to fully commit files...");
            await Task.Delay(2000, cancellationToken);

            var input = new DocumentTranslationInput(sourceUri, targetUri, targetLang);
            
            if (!autoDetect && !string.IsNullOrEmpty(sourceLanguage))
            {
                _logger.LogInformation("Creating translation input with source language: {SourceLang}", sourceLanguage);
                var translationSource = new TranslationSource(sourceUri) { LanguageCode = sourceLanguage };
                var translationTarget = new TranslationTarget(targetUri, targetLang);
                input = new DocumentTranslationInput(translationSource, new[] { translationTarget });
            }
            else
            {
                _logger.LogInformation("Creating translation input with auto-detect (no source language specified)");
            }
            
            var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);
            
            if (operation == null || string.IsNullOrEmpty(operation.Id))
                throw new InvalidOperationException("Translation operation was created but returned no operation ID");
            
            _logger.LogInformation("Batch translation started with operation ID: {OperationId}", operation.Id);

            // Cache the operation for status tracking (thread-safe)
            _activeOperations[operation.Id] = operation;

            _logger.LogInformation("Waiting for operation to complete...");
            await operation.WaitForCompletionAsync(cancellationToken);

            _logger.LogInformation("Operation completed with status: {Status}", operation.Status);
            _logger.LogInformation("Documents - Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}", 
                operation.DocumentsTotal, operation.DocumentsSucceeded, operation.DocumentsFailed);

            // Log any errors
            await foreach (var document in operation.GetDocumentStatusesAsync())
            {
                if (document.Status == Azure.AI.Translation.Document.DocumentTranslationStatus.Failed)
                {
                    _logger.LogError("Document failed: {SourceUri}, Error: {ErrorCode} - {ErrorMessage}",
                        document.SourceDocumentUri, document.Error?.Code, document.Error?.Message);
                }
            }

            return operation.Id;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure RequestFailedException: Status={Status}, ErrorCode={ErrorCode}, Message={Message}", 
                ex.Status, ex.ErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting batch translation: {Message}", ex.Message);
            throw;
        }
    }

    private async Task<string> StartBatchTranslationWithoutWaitingAsync(
        string sourceContainerName, 
        string targetContainerName, 
        string? sourceLanguage,
        List<string> targetLanguages, 
        bool autoDetect, 
        string jobId,
        bool hasImageProcessing,
        List<IFormFile> originalFiles,
        string targetLanguageCode,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting batch translation (async) - Source: {SourceContainer}, Target: {TargetContainer}", 
                sourceContainerName, targetContainerName);

            var blobUri = new Uri($"https://{_blobSettings.AccountName}.blob.core.windows.net");
            var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(blobUri, _credentialService.GetBlobStorageCredential());

            var sourceContainerClient = blobServiceClient.GetBlobContainerClient(sourceContainerName);
            var targetContainerClient = blobServiceClient.GetBlobContainerClient(targetContainerName);
            
            var sourceExists = await sourceContainerClient.ExistsAsync(cancellationToken);
            if (!sourceExists.Value)
            {
                throw new InvalidOperationException($"Source container {sourceContainerName} should exist after file upload!");
            }
            
            await CreateContainerWithRetryAsync(targetContainerClient, targetContainerName, cancellationToken);
            _logger.LogInformation("Target container {Container} ready", targetContainerName);

            var sourceUri = sourceContainerClient.Uri;
            var targetUri = targetContainerClient.Uri;

            _logger.LogInformation("Translation URIs - Source: {SourceUri}, Target: {TargetUri}", sourceUri, targetUri);

            var targetLang = targetLanguages.First();
            _logger.LogInformation("Starting translation for language: {TargetLanguage}", targetLang);

            await Task.Delay(2000, cancellationToken);

            var input = new DocumentTranslationInput(sourceUri, targetUri, targetLang);
            
            if (!autoDetect && !string.IsNullOrEmpty(sourceLanguage))
            {
                _logger.LogInformation("Using source language: {SourceLang}", sourceLanguage);
                var translationSource = new TranslationSource(sourceUri) { LanguageCode = sourceLanguage };
                var translationTarget = new TranslationTarget(targetUri, targetLang);
                input = new DocumentTranslationInput(translationSource, new[] { translationTarget });
            }
            
            var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);
            
            if (operation == null || string.IsNullOrEmpty(operation.Id))
                throw new InvalidOperationException("Translation operation created but no operation ID returned");
            
            _logger.LogInformation("Batch translation started with operation ID: {OperationId}", operation.Id);

            // Cache the operation using thread-safe method
            _activeOperations[operation.Id] = operation;
            
            // Update or create metadata using thread-safe AddOrUpdate
            _jobMetadata.AddOrUpdate(
                jobId,
                // Add factory - create new metadata if doesn't exist
                key => new JobMetadata
                {
                    JobId = jobId,
                    OperationId = operation.Id,
                    AllOperationIds = new List<string> { operation.Id },
                    HasImageProcessing = hasImageProcessing,
                    SourceContainerName = sourceContainerName,
                    OriginalFiles = originalFiles,
                    TargetLanguages = targetLanguages,
                    CurrentPhase = "Starting",
                    TargetContainersByLanguage = new Dictionary<string, string> { { targetLanguageCode, targetContainerName } },
                    OperationIdToLanguage = new Dictionary<string, string> { { operation.Id, targetLanguageCode } }
                },
                // Update factory - update existing metadata
                (key, existing) =>
                {
                    // Store ALL operation IDs
                    existing.AllOperationIds.Add(operation.Id);
                    
                    // Set primary operation ID if this is the first one
                    if (string.IsNullOrEmpty(existing.OperationId))
                    {
                        existing.OperationId = operation.Id;
                    }
                    
                    // Map this language to its target container and operation
                    existing.TargetContainersByLanguage[targetLanguageCode] = targetContainerName;
                    existing.OperationIdToLanguage[operation.Id] = targetLanguageCode;
                    
                    return existing;
                });
            
            _logger.LogInformation("Stored metadata for job {JobId}, language {Language}, container {Container}, operation {OperationId}", 
                jobId, targetLanguageCode, targetContainerName, operation.Id);

            return operation.Id;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure RequestFailedException: Status={Status}, ErrorCode={ErrorCode}", 
                ex.Status, ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting batch translation: {Message}", ex.Message);
            throw;
        }
    }

    private async Task MonitorTranslationAndProcessImagesAsync(
        string jobId, 
        string operationId, 
        string targetContainerName, 
        List<IFormFile> originalFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting background monitoring for job {JobId}, operation {OperationId}", jobId, operationId);

            DocumentTranslationOperation? operation;
            _activeOperations.TryGetValue(operationId, out operation);

            if (operation == null)
            {
                _logger.LogError("Operation {OperationId} not found in cache for job {JobId}", operationId, jobId);
                return;
            }

            // Don't update phase here - it's already set by ProcessBatchTranslationAsync to "Translating Documents"
            // Just wait for translation to complete

            // Wait for translation to complete
            await operation.WaitForCompletionAsync(cancellationToken);

            _logger.LogInformation("Translation completed for job {JobId} with status: {Status}", 
                jobId, operation.Status);

            // Update phase to image replacement if successful
            if (operation.Status == DocumentTranslationStatus.Succeeded)
            {
                UpdateJobPhase(jobId, "Replacing Images");
                
                // Process image replacement
                await ProcessImageReplacementAfterTranslationAsync(originalFiles, targetContainerName, jobId, cancellationToken);
                
                _logger.LogInformation("Image replacement completed for job {JobId}", jobId);
                UpdateJobPhase(jobId, "Completed");
            }
            else
            {
                _logger.LogWarning("Translation failed for job {JobId} with status: {Status}", jobId, operation.Status);
                UpdateJobPhase(jobId, "Failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in background monitoring for job {JobId}", jobId);
            UpdateJobPhase(jobId, "Error");
        }
    }

    /// <summary>
    /// Monitors ALL translation operations for a multi-language job and processes image replacement for each.
    /// This replaces the per-operation monitoring to avoid creating multiple concurrent tasks.
    /// </summary>
    private async Task MonitorAllTranslationsAndProcessImagesAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting unified background monitoring for job {JobId}", jobId);

            // Get all operations for this job
            if (!_jobMetadata.TryGetValue(jobId, out var metadata))
            {
                _logger.LogError("Job metadata not found for {JobId}", jobId);
                return;
            }
            
            var operationIds = new List<string>(metadata.AllOperationIds);
            var originalFiles = metadata.OriginalFiles;
            var containersByLanguage = new Dictionary<string, string>(metadata.TargetContainersByLanguage);
            var operationToLanguage = new Dictionary<string, string>(metadata.OperationIdToLanguage);

            _logger.LogInformation("Monitoring {OperationCount} operations for job {JobId}", operationIds.Count, jobId);

            // Wait for ALL operations to complete
            var completedOperations = new Dictionary<string, DocumentTranslationStatus>();
            
            foreach (var operationId in operationIds)
            {
                if (!_activeOperations.TryGetValue(operationId, out var operation))
                {
                    _logger.LogError("Operation {OperationId} not found in cache for job {JobId}", operationId, jobId);
                    completedOperations[operationId] = DocumentTranslationStatus.Failed;
                    continue;
                }

                try
                {
                    var languageCode = operationToLanguage.TryGetValue(operationId, out var lang) ? lang : "unknown";
                    _logger.LogInformation("Waiting for operation {OperationId} (language: {Language}) to complete", 
                        operationId, languageCode);
                    
                    await operation.WaitForCompletionAsync(cancellationToken);
                    completedOperations[operationId] = operation.Status;
                    
                    _logger.LogInformation("Operation {OperationId} completed with status: {Status}", 
                        operationId, operation.Status);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error waiting for operation {OperationId}", operationId);
                    completedOperations[operationId] = DocumentTranslationStatus.Failed;
                }
            }

            // Check if all operations succeeded
            var allSucceeded = completedOperations.Values.All(s => s == DocumentTranslationStatus.Succeeded);
            var anyFailed = completedOperations.Values.Any(s => s == DocumentTranslationStatus.Failed);

            if (allSucceeded)
            {
                _logger.LogInformation("All {Count} operations succeeded for job {JobId}, starting image replacement", 
                    operationIds.Count, jobId);
                
                UpdateJobPhase(jobId, JobPhases.ReplacingImages);
                
                // Process image replacement for EACH target container
                foreach (var kvp in containersByLanguage)
                {
                    var language = kvp.Key;
                    var targetContainerName = kvp.Value;
                    
                    try
                    {
                        _logger.LogInformation("Processing image replacement for language {Language} in container {Container}", 
                            language, targetContainerName);
                        
                        await ProcessImageReplacementAfterTranslationAsync(
                            originalFiles, 
                            targetContainerName, 
                            jobId, 
                            cancellationToken);
                        
                        _logger.LogInformation("Image replacement completed for language {Language}", language);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing image replacement for language {Language}", language);
                    }
                }
                
                _logger.LogInformation("All image replacement completed for job {JobId}", jobId);
                UpdateJobPhase(jobId, JobPhases.Completed);
            }
            else if (anyFailed)
            {
                _logger.LogWarning("Some operations failed for job {JobId}", jobId);
                UpdateJobPhase(jobId, JobPhases.Failed);
            }
            else
            {
                _logger.LogWarning("Translations completed with mixed status for job {JobId}", jobId);
                UpdateJobPhase(jobId, JobPhases.Completed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in unified background monitoring for job {JobId}", jobId);
            UpdateJobPhase(jobId, JobPhases.Error);
        }
    }

    private void UpdateJobPhase(string jobId, string phase)
    {
        _jobMetadata.AddOrUpdate(
            jobId,
            // Add factory - shouldn't happen, but provides safety
            key => new JobMetadata
            {
                JobId = jobId,
                CurrentPhase = phase,
                LastPhaseUpdate = DateTime.UtcNow
            },
            // Update factory
            (key, existing) =>
            {
                existing.CurrentPhase = phase;
                existing.LastPhaseUpdate = DateTime.UtcNow;
                return existing;
            });
        
        _logger.LogInformation("Job {JobId} phase updated to: {Phase}", jobId, phase);
    }
    
    public async Task<JobStatus> GetTranslationStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if we have a cached terminal status for this job
            if (_terminalJobsCache.TryGetValue(jobId, out var cachedStatus))
            {
                // Check if cache is still valid
                if (DateTime.UtcNow - cachedStatus.CachedAt < _cacheExpiration)
                {
                    _logger.LogInformation("Returning cached status for terminal job {JobId}: {Status}", 
                        jobId, cachedStatus.Status.Status);
                    return cachedStatus.Status;
                }
                else
                {
                    // Cache expired, remove it
                    _terminalJobsCache.TryRemove(jobId, out _);
                }
            }

            _logger.LogInformation("Checking status for translation job {JobId}", jobId);

            // Get ALL Azure operationIds from our jobId using metadata
            List<string> allOperationIds = new();
            string? primaryOperationId = null;
            
            if (_jobMetadata.TryGetValue(jobId, out var metadata))
            {
                primaryOperationId = metadata.OperationId;
                allOperationIds = new List<string>(metadata.AllOperationIds);
                _logger.LogInformation("Found {OperationCount} operation(s) for jobId {JobId}", allOperationIds.Count, jobId);
            }


            if (string.IsNullOrEmpty(primaryOperationId) || !allOperationIds.Any())
            {
                _logger.LogWarning("No operationIds found for jobId {JobId}, job may not have started yet or metadata was lost", jobId);
                return new JobStatus
                {
                    JobId = jobId,
                    Status = "NotFound",
                    ErrorMessage = $"Job metadata not found for: {jobId}",
                    DetailedStatus = "Job information not found. The job may not have started yet or was never created.",
                    CurrentPhase = "Not Found"
                };
            }

            // Collect status for all operations
            var operationStatuses = new Dictionary<string, Azure.AI.Translation.Document.TranslationStatusResult>();
            
            await foreach (var status in _batchClient.GetTranslationStatusesAsync(cancellationToken: cancellationToken))
            {
                if (allOperationIds.Contains(status.Id))
                {
                    // Store the Azure SDK's TranslationStatusResult directly
                    operationStatuses[status.Id] = status;
                    _logger.LogInformation("Found status for operation {OperationId}: {Status}", status.Id, status.Status);
                }
                
                if (operationStatuses.Count == allOperationIds.Count)
                {
                    break;
                }
            }
            
            if (!operationStatuses.Any())
            {
                _logger.LogWarning("Translation operations not found in Azure for job {JobId}", jobId);
                return new JobStatus
                {
                    JobId = jobId,
                    Status = "NotFound",
                    ErrorMessage = $"Translation operations not found: {jobId}",
                    DetailedStatus = "Operations not found in Azure Translation Service. They may have been deleted or never existed.",
                    CurrentPhase = "Not Found"
                };
            }
            
            var aggregatedStatus = AggregateOperationStatuses(operationStatuses.Values);
            var statusString = aggregatedStatus.Status;
            
            // Check for custom phase and target languages from job metadata
            string? customPhase = null;
            List<string>? targetLanguages = null;
            DateTime? jobCreatedAt = null;
            
            if (_jobMetadata.TryGetValue(jobId, out var currentMetadata))
            {
                customPhase = currentMetadata.CurrentPhase;
                targetLanguages = currentMetadata.TargetLanguages;
                jobCreatedAt = currentMetadata.CreatedAt;
            }

            var jobStatus = new JobStatus
            {
                JobId = jobId,
                Status = statusString,
                TotalDocuments = aggregatedStatus.TotalDocuments,
                TranslatedDocuments = aggregatedStatus.TranslatedDocuments,
                FailedDocuments = aggregatedStatus.FailedDocuments,
                DocumentsInProgress = aggregatedStatus.DocumentsInProgress,
                DocumentsNotStarted = aggregatedStatus.DocumentsNotStarted,
                CreatedOn = aggregatedStatus.CreatedOn,
                LastModified = aggregatedStatus.LastModified,
                ElapsedTime = jobCreatedAt.HasValue 
                    ? DateTime.UtcNow - jobCreatedAt.Value 
                    : aggregatedStatus.LastModified - aggregatedStatus.CreatedOn
            };

            jobStatus.CurrentPhase = customPhase ?? DetermineCurrentPhase(statusString, jobStatus);
            jobStatus.TargetLanguages = targetLanguages ?? new List<string>();
            
            bool hasImageProcessing = false;
            if (_jobMetadata.TryGetValue(jobId, out var imageMetadata))
            {
                hasImageProcessing = imageMetadata.HasImageProcessing;
            }
            
            jobStatus.PercentComplete = CalculateOverallProgress(jobStatus, hasImageProcessing);
            jobStatus.DetailedStatus = BuildDetailedStatusMessage(jobStatus);

            // Handle ValidationFailed status with detailed error information
            if (statusString == "ValidationFailed" || statusString == "Failed" || jobStatus.FailedDocuments > 0)
            {
                _logger.LogWarning("Translation job {JobId} has status {Status} with {Failed} failed documents", 
                    jobId, statusString, jobStatus.FailedDocuments);
                
                var errorDetails = await GetDocumentErrorDetailsAsync(jobId, cancellationToken);
                
                if (!string.IsNullOrEmpty(errorDetails))
                {
                    jobStatus.ErrorMessage = errorDetails;
                    _logger.LogError("Detailed errors for job {JobId}:\n{ErrorDetails}", jobId, errorDetails);
                }
                else if (statusString == "ValidationFailed")
                {
                    jobStatus.ErrorMessage = "Validation failed: Azure Translation Service cannot access the blob storage. " +
                        "This usually means:\n" +
                        "1. The Translation Service's managed identity doesn't have 'Storage Blob Data Contributor' role\n" +
                        "2. Role assignment hasn't propagated yet (wait 5-10 minutes)\n" +
                        "3. Blob storage URIs are incorrect\n\n" +
                        $"Job ID: {jobId}\n" +
                        "See MANAGED_IDENTITY_SETUP.md for instructions.";
                }
                else
                {
                    jobStatus.ErrorMessage = $"{jobStatus.FailedDocuments} document(s) failed to translate";
                }
                
                if (statusString == "ValidationFailed" || statusString == "Failed")
                {
                    CacheTerminalStatus(jobId, jobStatus);
                }
                
                return jobStatus;
            }

            // Handle cancelled status
            if (statusString == "Cancelled")
            {
                jobStatus.ErrorMessage = "Translation job was cancelled";
                _logger.LogWarning("Translation job {JobId} was cancelled", jobId);
                CacheTerminalStatus(jobId, jobStatus);
                return jobStatus;
            }

            // Handle successful completion
            if (statusString == "Succeeded")
            {
                _logger.LogInformation("Translation job {JobId} completed successfully: {Succeeded}/{Total} documents", 
                    jobId, jobStatus.TranslatedDocuments, jobStatus.TotalDocuments);
                
                if (jobStatus.CurrentPhase != "Completed")
                {
                    _logger.LogInformation("Job {JobId} translation succeeded but still in phase: {Phase}", 
                        jobId, jobStatus.CurrentPhase);
                    
                    if (jobStatus.CurrentPhase == "Replacing Images" || 
                        jobStatus.CurrentPhase == "Translating Documents")
                    {
                        jobStatus.Status = "Processing";
                    }
                }
                else
                {
                    _logger.LogInformation("Job {JobId} fully completed, caching terminal status", jobId);
                    CacheTerminalStatus(jobId, jobStatus);
                }
            }

            _logger.LogInformation("Translation job {JobId} status: {Status}, Phase: {Phase}, Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}", 
                jobId, jobStatus.Status, jobStatus.CurrentPhase, jobStatus.TotalDocuments, jobStatus.TranslatedDocuments, jobStatus.FailedDocuments);

            return jobStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting translation status for job {JobId}", jobId);
            return new JobStatus
            {
                JobId = jobId,
                Status = "Error",
                ErrorMessage = $"Error retrieving job status: {ex.Message}",
                DetailedStatus = "An error occurred while checking the translation status.",
                CurrentPhase = "Error"
            };
        }
    }

    /// <summary>
    /// Aggregates status across multiple translation operations for multi-language jobs.
    /// </summary>
    private (string Status, int TotalDocuments, int TranslatedDocuments, int FailedDocuments, 
             int DocumentsInProgress, int DocumentsNotStarted, DateTimeOffset CreatedOn, DateTimeOffset LastModified) 
        AggregateOperationStatuses(IEnumerable<Azure.AI.Translation.Document.TranslationStatusResult> statuses)
    {
        var statusList = statuses.ToList();
        
        // Aggregate document counts across all operations
        int totalDocs = statusList.Sum(s => s.DocumentsTotal);
        int succeededDocs = statusList.Sum(s => s.DocumentsSucceeded);
        int failedDocs = statusList.Sum(s => s.DocumentsFailed);
        int inProgressDocs = statusList.Sum(s => s.DocumentsInProgress);
        int notStartedDocs = statusList.Sum(s => s.DocumentsNotStarted);
        
        // Use earliest CreatedOn and latest LastModified
        var createdOn = statusList.Min(s => s.CreatedOn);
        var lastModified = statusList.Max(s => s.LastModified);
        
        // Determine overall status:
        // - If ANY operation failed/validation failed, overall is Failed
        // - If ALL operations succeeded, overall is Succeeded
        // - If ANY operation is still running, overall is Running
        // - If ALL operations are not started, overall is NotStarted
        // - If ANY operation is cancelled, overall is Cancelled
        string overallStatus;
        
        var statusStrings = statusList.Select(s => s.Status.ToString()).ToList();
        
        if (statusStrings.Any(s => s == "ValidationFailed" || s == "Failed"))
        {
            overallStatus = statusStrings.Contains("ValidationFailed") ? "ValidationFailed" : "Failed";
        }
        else if (statusStrings.All(s => s == "Succeeded"))
        {
            overallStatus = "Succeeded";
        }
        else if (statusStrings.Any(s => s == "Running"))
        {
            overallStatus = "Running";
        }
        else if (statusStrings.All(s => s == "NotStarted"))
        {
            overallStatus = "NotStarted";
        }
        else if (statusStrings.Any(s => s == "Cancelled"))
        {
            overallStatus = "Cancelled";
        }
        else
        {
            overallStatus = "Running"; // Default fallback
        }
        
        _logger.LogInformation("Aggregated status across {OperationCount} operations: {Status} ({Succeeded}/{Total} documents)", 
            statusList.Count, overallStatus, succeededDocs, totalDocs);
        
        return (overallStatus, totalDocs, succeededDocs, failedDocs, inProgressDocs, notStartedDocs, createdOn, lastModified);
    }

    private string DetermineCurrentPhase(string status, JobStatus jobStatus)
    {
        return status switch
        {
            "NotStarted" => "Initializing",
            "Running" when jobStatus.DocumentsNotStarted == jobStatus.TotalDocuments => "Starting",
            "Running" when jobStatus.DocumentsInProgress > 0 => "Translating",
            "Running" => "Processing",
            "Succeeded" => "Completed",
            "Failed" => "Failed",
            "Cancelled" => "Cancelled",
            "ValidationFailed" => "Validation Failed",
            _ => status
        };
    }

    private int CalculateOverallProgress(JobStatus jobStatus, bool hasImageProcessing)
    {
        // If no image processing, use simple document-based progress
        if (!hasImageProcessing)
        {
            if (jobStatus.TotalDocuments > 0)
            {
                return (int)((double)jobStatus.TranslatedDocuments / jobStatus.TotalDocuments * 100);
            }
            return 0;
        }

        // With image processing, we have multiple phases:
        // 1. Uploading Files (0-10%)
        // 2. Extracting Images (10-20%)
        // 3. Starting (20-25%)
        // 4. Translating Documents (25-85%) - 60% of total
        // 5. Replacing Images (85-95%)
        // 6. Completed (100%)

        return jobStatus.CurrentPhase switch
        {
            "Initializing" => 0,
            "Uploading Files" => 5,
            "Extracting Images" => 15,
            "Starting" or "Starting Translation" => 22,
            "Translating Documents" or "Translating" or "Processing" => 
                // Map document translation progress (0-100%) to 25-85% of overall progress
                25 + (int)((jobStatus.TotalDocuments > 0 
                    ? (double)jobStatus.TranslatedDocuments / jobStatus.TotalDocuments 
                    : 0) * 60),
            "Replacing Images" => 90,
            "Completed" => 100,
            "Failed" or "Cancelled" or "Validation Failed" => jobStatus.PercentComplete,
            _ => jobStatus.TotalDocuments > 0 
                ? (int)((double)jobStatus.TranslatedDocuments / jobStatus.TotalDocuments * 100) 
                : 0
        };
    }

    private string BuildDetailedStatusMessage(JobStatus jobStatus)
    {
        var messages = new List<string>();

        // Add phase-specific message
        switch (jobStatus.CurrentPhase)
        {
            case "Initializing":
                messages.Add("Initializing translation job...");
                break;
            case "Starting":
            case "Starting Translation":
                messages.Add("Starting document translation...");
                break;
            case "Uploading Files":
                messages.Add("Uploading files to storage...");
                break;
            case "Extracting Images":
                messages.Add("Extracting images from documents...");
                break;
            case "Translating Documents":
                messages.Add($"Translating documents... ({jobStatus.TranslatedDocuments}/{jobStatus.TotalDocuments} completed)");
                break;
            case "Translating":
                messages.Add($"Translating documents... ({jobStatus.TranslatedDocuments}/{jobStatus.TotalDocuments} completed)");
                break;
            case "Processing":
                messages.Add($"Processing documents... ({jobStatus.TranslatedDocuments}/{jobStatus.TotalDocuments} completed)");
                break;
            case "Replacing Images":
                messages.Add("Replacing translated images in documents...");
                break;
            case "Completed":
                messages.Add($"Translation completed successfully! All {jobStatus.TotalDocuments} document(s) translated.");
                break;
            case "Failed":
                messages.Add($"Translation failed. {jobStatus.FailedDocuments} document(s) failed.");
                break;
            case "Cancelled":
                messages.Add("Translation job was cancelled.");
                break;
            case "Validation Failed":
                messages.Add("Validation failed. Check permissions and configuration.");
                break;
        }

        // Add progress details for active jobs
        if (jobStatus.CurrentPhase == "Translating" || jobStatus.CurrentPhase == "Processing")
        {
            if (jobStatus.DocumentsInProgress > 0)
            {
                messages.Add($"   • In Progress: {jobStatus.DocumentsInProgress}");
            }
            if (jobStatus.DocumentsNotStarted > 0)
            {
                messages.Add($"   • Pending: {jobStatus.DocumentsNotStarted}");
            }
            if (jobStatus.FailedDocuments > 0)
            {
                messages.Add($"   • Failed: {jobStatus.FailedDocuments}");
            }
        }

        // Add elapsed time if available
        if (jobStatus.ElapsedTime.HasValue)
        {
            var elapsed = jobStatus.ElapsedTime.Value;
            var timeStr = elapsed.TotalMinutes < 1 
                ? $"{elapsed.Seconds} second(s)" 
                : $"{(int)elapsed.TotalMinutes} minute(s) {elapsed.Seconds} second(s)";
            
            messages.Add($"Elapsed time: {timeStr}");
        }

        return string.Join("\n", messages);
    }

    private async Task<string> GetDocumentErrorDetailsAsync(string jobId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retrieving detailed error information for job {JobId}", jobId);
            
            // Get the Azure operationId from our jobId
            string? operationId = null;
            if (_jobMetadata.TryGetValue(jobId, out var metadata))
            {
                operationId = metadata.OperationId;
            }

            if (string.IsNullOrEmpty(operationId))
            {
                _logger.LogWarning("No operationId found for jobId {JobId}", jobId);
                return "Job metadata not found. Cannot retrieve error details.";
            }
            
            // Find the job status in the list to get basic information
            Azure.AI.Translation.Document.TranslationStatusResult? foundStatusItem = null;
            
            await foreach (var statusItem in _batchClient.GetTranslationStatusesAsync(cancellationToken: cancellationToken))
            {
                if (statusItem.Id == operationId)
                {
                    foundStatusItem = statusItem;
                    _logger.LogInformation("Found operation status for job {JobId} (operationId {OperationId}): {Status}, Failed: {Failed}, NotStarted: {NotStarted}", 
                        jobId, operationId, statusItem.Status, statusItem.DocumentsFailed, statusItem.DocumentsNotStarted);
                    break;
                }
            }
            
            if (foundStatusItem == null)
            {
                _logger.LogWarning("Operation {OperationId} for job {JobId} not found in Azure Translation Service", operationId, jobId);
                return "Operation not found. The job may not exist or may have been deleted.";
            }
            
            // Try to get document-level details only if we have a cached operation
            DocumentTranslationOperation? cachedOperation = null;
            _activeOperations.TryGetValue(operationId, out cachedOperation);
            
            if (cachedOperation != null)
            {
                _logger.LogInformation("Found cached operation for job {JobId}, attempting to get document details", jobId);
                
                try
                {
                    var errorMessages = new List<string>();
                    var documentCount = 0;
                    
                    await foreach (var document in cachedOperation.GetDocumentStatusesAsync())
                    {
                        documentCount++;
                        
                        if (document.Status == Azure.AI.Translation.Document.DocumentTranslationStatus.Failed ||
                            document.Status == Azure.AI.Translation.Document.DocumentTranslationStatus.ValidationFailed)
                        {
                            var errorMsg = document.Status == Azure.AI.Translation.Document.DocumentTranslationStatus.ValidationFailed
                                ? $"Document validation failed: {document.SourceDocumentUri?.AbsolutePath ?? document.SourceDocumentUri?.ToString() ?? "Unknown"}"
                                : $"Document failed: {document.SourceDocumentUri?.AbsolutePath ?? document.SourceDocumentUri?.ToString() ?? "Unknown"}";
                            
                            if (document.Error != null)
                            {
                                errorMsg += $"\n  Error Code: {document.Error.Code}";
                                errorMsg += $"\n  Message: {document.Error.Message}";
                            }
                            else
                            {
                                errorMsg += "\n  No detailed error information available";
                            }
                            
                            errorMessages.Add(errorMsg);
                            _logger.LogError("Document error: {ErrorMessage}", errorMsg);
                        }
                    }
                    
                    if (errorMessages.Count > 0)
                    {
                        _logger.LogInformation("Retrieved {Count} document-level errors for job {JobId}", errorMessages.Count, jobId);
                        return string.Join("\n\n", errorMessages);
                    }
                    
                    _logger.LogInformation("Processed {DocumentCount} documents but found no error details", documentCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not retrieve document details from cached operation for job {JobId}", jobId);
                }
            }
            else
            {
                _logger.LogInformation("No cached operation available for job {JobId}, cannot retrieve document-level details", jobId);
            }
            
            // Provide detailed fallback information based on the status
            var statusString = foundStatusItem.Status.ToString();
            
            if (statusString == "ValidationFailed")
            {
                return BuildValidationFailedMessage(foundStatusItem);
            }
            else if (foundStatusItem.DocumentsFailed > 0)
            {
                return BuildDocumentFailedMessage(foundStatusItem);
            }
            
            // No specific error details available
            return $"Job Status: {statusString}\n" +
                   $"Total Documents: {foundStatusItem.DocumentsTotal}\n" +
                   $"Succeeded: {foundStatusItem.DocumentsSucceeded}\n" +
                   $"Failed: {foundStatusItem.DocumentsFailed}\n" +
                   $"In Progress: {foundStatusItem.DocumentsInProgress}\n" +
                   $"Not Started: {foundStatusItem.DocumentsNotStarted}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document error details for job {JobId}", jobId);
            return $"Could not retrieve detailed error information: {ex.Message}";
        }
    }
    
    private string BuildValidationFailedMessage(Azure.AI.Translation.Document.TranslationStatusResult status)
    {
        var message = $"Validation Failed\n\n";
        message += $"Total Documents: {status.DocumentsTotal}\n";
        message += $"Documents Not Started: {status.DocumentsNotStarted}\n";
        message += $"Failed Documents: {status.DocumentsFailed}\n\n";
        
        message += "Common causes of validation failure:\n\n";
        
        message += "1. PERMISSION ISSUES (Most Common)\n";
        message += "   The Azure Translation Service cannot access your blob storage.\n";
        message += "   Required: 'Storage Blob Data Contributor' role on the storage account.\n\n";
        
        message += "   To fix:\n";
        message += "   - Go to Azure Portal ? Your Storage Account ? Access Control (IAM)\n";
        message += "   - Click '+ Add' ? 'Add role assignment'\n";
        message += "   - Select 'Storage Blob Data Contributor' role\n";
        message += "   - Assign to your Translation Service's managed identity\n";
        message += "   - Wait 5-10 minutes for permission propagation\n\n";
        
        message += "2. STORAGE ACCOUNT FIREWALL\n";
        message += "   If your storage account has firewall rules:\n";
        message += "   - Add the Translation Service's subnet to allowed networks\n";
        message += "   - Or enable 'Allow Azure services on the trusted services list'\n\n";
        
        message += "3. INCORRECT URIS\n";
        message += "   Verify the source and target blob URIs are correct:\n";
        message += $"   - Storage Account: {_blobSettings.AccountName}\n";
        message += $"   - Container: {_blobSettings.ContainerName}\n";
        message += "   - Check for typos in account name or container name\n\n";
        
        message += "4. CONTAINER DOES NOT EXIST\n";
        message += "   Ensure the container exists in the storage account\n\n";
        
        message += "5. FILES NOT ACCESSIBLE\n";
        message += "   Verify the source files exist at the specified location\n\n";
        
        message += $"Job ID: {status.Id}\n";
        message += $"Created: {status.CreatedOn:yyyy-MM-dd HH:mm:ss} UTC";
        
        return message;
    }
    
    private string BuildDocumentFailedMessage(Azure.AI.Translation.Document.TranslationStatusResult status)
    {
        var message = $"Translation Failed\n\n";
        message += $"Total Documents: {status.DocumentsTotal}\n";
        message += $"Succeeded: {status.DocumentsSucceeded}\n";
        message += $"Failed: {status.DocumentsFailed}\n";
        message += $"In Progress: {status.DocumentsInProgress}\n\n";
        
        message += "Common causes of document translation failure:\n\n";
        
        message += "1. UNSUPPORTED DOCUMENT FORMAT\n";
        message += "   The document may be corrupted or in an unsupported format\n\n";
        
        message += "2. DOCUMENT TOO LARGE\n";
        message += "   Document exceeds the size limit (typically 40 MB per file)\n\n";
        
        message += "3. UNSUPPORTED LANGUAGE PAIR\n";
        message += "   The requested translation direction may not be supported\n\n";
        
        message += "4. PROTECTED/ENCRYPTED DOCUMENTS\n";
        message += "   Password-protected or DRM-protected documents cannot be translated\n\n";
        
        message += "5. DOCUMENT STRUCTURE ISSUES\n";
        message += "   Complex formatting or embedded objects may cause failures\n\n";
        
        message += $"Job ID: {status.Id}\n";
        message += $"Last Modified: {status.LastModified:yyyy-MM-dd HH:mm:ss} UTC\n\n";
        
        message += "Note: Document-level error details are only available during active job processing.\n";
        message += "Check the Azure Portal ? Translation Service ? Document Translation for more details.";
        
        return message;
    }

    private void CacheTerminalStatus(string jobId, JobStatus status)
    {
        _terminalJobsCache[jobId] = (status, DateTime.UtcNow);
        _logger.LogInformation("Cached terminal status for job {JobId}: {Status}", jobId, status.Status);
    }
    
    private async Task<string> GetLanguageNameAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        // Check if cache is still valid (24 hours)
        lock (_languageCacheLock)
        {
            if (_languageNameCache.Count > 0 && DateTime.UtcNow < _languageCacheExpiration)
            {
                if (_languageNameCache.TryGetValue(languageCode, out var cachedName))
                {
                    return cachedName;
                }
            }
        }
        
        // Refresh cache if needed
        try
        {
            var languages = await _languageService.GetSupportedLanguagesAsync(cancellationToken);
            
            lock (_languageCacheLock)
            {
                // Clear and rebuild cache
                _languageNameCache.Clear();
                foreach (var lang in languages)
                {
                    _languageNameCache[lang.Code] = lang.Name;
                }
                _languageCacheExpiration = DateTime.UtcNow.AddHours(24);
                
                if (_languageNameCache.TryGetValue(languageCode, out var name))
                {
                    return name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get language name for code {LanguageCode}", languageCode);
        }
        
        // Fallback to code if name not found
        return languageCode;
    }

    public async Task<List<TranslationJobInfo>> GetAllTranslationJobsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching all translation jobs");

            var jobs = new List<TranslationJobInfo>();
            
            await foreach (var statusResponse in _batchClient.GetTranslationStatusesAsync(cancellationToken: cancellationToken))
            {
                try
                {
                    var jobInfo = new TranslationJobInfo
                    {
                        Id = statusResponse.Id,
                        Status = statusResponse.Status.ToString(),
                        CreatedOn = statusResponse.CreatedOn,
                        LastModified = statusResponse.LastModified,
                        TotalDocuments = statusResponse.DocumentsTotal,
                        DocumentsSucceeded = statusResponse.DocumentsSucceeded,
                        DocumentsFailed = statusResponse.DocumentsFailed,
                        DocumentsInProgress = statusResponse.DocumentsInProgress,
                        DocumentsNotStarted = statusResponse.DocumentsNotStarted,
                        DocumentsCanceled = statusResponse.DocumentsCanceled
                    };

                    var statusString = statusResponse.Status.ToString();
                    
                    // Populate error messages for failed states
                    if (statusString == "ValidationFailed" || statusString == "Failed" || statusResponse.DocumentsFailed > 0)
                    {
                        // Try to get detailed error information
                        try
                        {
                            var errorDetails = await GetDocumentErrorDetailsAsync(statusResponse.Id, cancellationToken);
                            if (!string.IsNullOrEmpty(errorDetails))
                            {
                                jobInfo.ErrorMessage = errorDetails;
                            }
                            else if (statusString == "ValidationFailed")
                            {
                                // Provide detailed validation failure message if no document-level errors available
                                jobInfo.ErrorMessage = BuildValidationFailedMessage(statusResponse);
                            }
                            else if (statusResponse.DocumentsFailed > 0)
                            {
                                jobInfo.ErrorMessage = BuildDocumentFailedMessage(statusResponse);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not retrieve detailed error for job {JobId}", statusResponse.Id);
                            // Fallback to simple error message
                            if (statusString == "ValidationFailed")
                            {
                                jobInfo.ErrorMessage = "Validation failed: Translation Service cannot access blob storage. Check managed identity permissions.";
                            }
                            else if (statusResponse.DocumentsFailed > 0)
                            {
                                jobInfo.ErrorMessage = $"{statusResponse.DocumentsFailed} document(s) failed to translate";
                            }
                        }
                    }
                    else if (statusString == "Cancelled")
                    {
                        jobInfo.ErrorMessage = "Translation job was cancelled";
                    }

                    jobs.Add(jobInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing translation status");
                }
            }

            _logger.LogInformation("Retrieved {JobCount} translation jobs", jobs.Count);
            return jobs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching translation jobs");
            throw;
        }
    }

    public async Task<bool> CancelTranslationJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Canceling translation job {JobId}", jobId);

            DocumentTranslationOperation? operation;
            
            if (!_activeOperations.TryGetValue(jobId, out operation))
            {
                operation = new DocumentTranslationOperation(jobId, _batchClient);
            }

            await operation.UpdateStatusAsync(cancellationToken);

            if (!operation.HasCompleted)
            {
                await operation.CancelAsync(cancellationToken);
                
                _activeOperations.TryRemove(jobId, out _);

                _logger.LogInformation("Translation job {JobId} canceled successfully", jobId);
                return true;
            }
            else
            {
                _logger.LogWarning("Cannot cancel job {JobId} - already completed with status {Status}", 
                    jobId, operation.Status);
                return false;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Job {JobId} not found for cancellation", jobId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling translation job {JobId}", jobId);
            throw;
        }
    }

    public async Task<List<bool>> CancelTranslationJobsAsync(List<string> jobIds, CancellationToken cancellationToken = default)
    {
        var results = new List<bool>();
        
        foreach (var jobId in jobIds)
        {
            try
            {
                var result = await CancelTranslationJobAsync(jobId, cancellationToken);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error canceling job {JobId}", jobId);
                results.Add(false);
            }
        }

        return results;
    }

    /// <summary>
    /// Checks if containers already exist (shouldn't with unique GUID) and cleans them up if found.
    /// This should only happen if leftover test data or debugging artifacts exist.
    /// </summary>
    private async Task CleanupExistingContainersIfNeededAsync(string sourceContainerName, string targetContainerName, CancellationToken cancellationToken)
    {
        // Create blob service client
        var blobUri = new Uri($"https://{_blobSettings.AccountName}.blob.core.windows.net");
        var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(blobUri, _credentialService.GetBlobStorageCredential());

        var sourceContainerClient = blobServiceClient.GetBlobContainerClient(sourceContainerName);
        var targetContainerClient = blobServiceClient.GetBlobContainerClient(targetContainerName);
        
        // Check if containers unexpectedly exist (shouldn't happen with unique GUIDs)
        var sourceExists = await sourceContainerClient.ExistsAsync(cancellationToken);
        var targetExists = await targetContainerClient.ExistsAsync(cancellationToken);
        
        if (sourceExists.Value || targetExists.Value)
        {
            // This is unusual - containers with this GUID already exist
            _logger.LogWarning("UNEXPECTED: Containers already exist! Source: {SourceContainer} ({SourceExists}), Target: {TargetContainer} ({TargetExists})", 
                sourceContainerName, sourceExists.Value, targetContainerName, targetExists.Value);
            _logger.LogWarning("This should not happen with unique GUIDs. Likely cause: leftover containers from previous run");
            
            // Log container details for debugging
            if (sourceExists.Value)
            {
                try
                {
                    var sourceProps = await sourceContainerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                    _logger.LogInformation("Existing source container - ETag: {ETag}, last modified: {LastModified}", 
                        sourceProps.Value.ETag, sourceProps.Value.LastModified);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not get properties of existing source container");
                }
            }
            
            // Clean up existing containers
            _logger.LogInformation("Cleaning up existing containers before uploading new files...");
            
            if (sourceExists.Value)
            {
                _logger.LogInformation("Deleting existing source container {Container}", sourceContainerName);
                await sourceContainerClient.DeleteAsync(cancellationToken: cancellationToken);
                await WaitForContainerDeletionAsync(sourceContainerClient, cancellationToken);
            }
            
            if (targetExists.Value)
            {
                _logger.LogInformation("Deleting existing target container {Container}", targetContainerName);
                await targetContainerClient.DeleteAsync(cancellationToken: cancellationToken);
                await WaitForContainerDeletionAsync(targetContainerClient, cancellationToken);
            }
            
            _logger.LogInformation("Cleanup complete, ready for fresh upload");
        }
        else
        {
            _logger.LogInformation("No existing containers found - starting fresh (as expected)");
        }
    }

    /// <summary>
    /// Waits for a container to be fully deleted from Azure Blob Storage.
    /// Container deletion is asynchronous and can take several seconds.
    /// </summary>
    private async Task WaitForContainerDeletionAsync(Azure.Storage.Blobs.BlobContainerClient containerClient, CancellationToken cancellationToken)
    {
        const int maxRetries = 30; // 30 seconds max wait
        const int delayMs = 1000; // 1 second between checks
        
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var exists = await containerClient.ExistsAsync(cancellationToken);
                if (!exists.Value)
                {
                    _logger.LogInformation("Container {ContainerName} deletion confirmed after {Seconds} seconds", 
                        containerClient.Name, i + 1);
                    return; // Container is fully deleted
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // 404 means container doesn't exist - deletion complete
                _logger.LogInformation("Container {ContainerName} deletion confirmed (404) after {Seconds} seconds", 
                    containerClient.Name, i + 1);
                return;
            }
            
            await Task.Delay(delayMs, cancellationToken);
        }
        
        _logger.LogWarning("Container {ContainerName} still exists after {Seconds} seconds, proceeding anyway", 
            containerClient.Name, maxRetries);
    }

    /// <summary>
    /// Creates a container with retry logic to handle the ContainerBeingDeleted transient error.
    /// </summary>
    private async Task CreateContainerWithRetryAsync(Azure.Storage.Blobs.BlobContainerClient containerClient, 
        string containerName, CancellationToken cancellationToken)
    {
        const int maxRetries = 10;
        const int baseDelayMs = 2000; // Start with 2 seconds
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                _logger.LogInformation("Successfully created/verified container {ContainerName}", containerName);
                return; // Success!
            }
            catch (Azure.RequestFailedException ex) when (ex.ErrorCode == "ContainerBeingDeleted")
            {
                if (attempt < maxRetries - 1)
                {
                    var delay = baseDelayMs * (attempt + 1); // Exponential backoff
                    _logger.LogWarning("Container {ContainerName} is still being deleted, waiting {DelaySeconds} seconds before retry {Attempt}/{MaxRetries}", 
                        containerName, delay / 1000, attempt + 1, maxRetries);
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    _logger.LogError("Container {ContainerName} still being deleted after {MaxRetries} retries", 
                        containerName, maxRetries);
                    throw; // Give up after max retries
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409 && attempt < maxRetries - 1)
            {
                // Other 409 conflicts - retry with backoff
                var delay = baseDelayMs * (attempt + 1);
                _logger.LogWarning("Conflict creating container {ContainerName}: {ErrorCode}, retrying in {DelaySeconds} seconds", 
                    containerName, ex.ErrorCode, delay / 1000);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
    
    /// <summary>
    /// Process image replacement for all translated files after translation completes.
    /// This runs BEFORE showing the download button to users.
    /// </summary>
    private async Task ProcessImageReplacementAfterTranslationAsync(
        List<IFormFile> originalFiles, 
        string targetContainerName, 
        string jobId, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting post-translation image replacement processing for job {JobId}", jobId);
        
        var metadataContainerName = ContainerNamePatterns.GetMetadataContainerName(jobId);
        
        foreach (var file in originalFiles)
        {
            var fileName = file.FileName;
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            
            // Only process supported file types
            if (extension != ".pdf" && extension != ".docx")
            {
                continue;
            }
            
            Stream? translatedDocStream = null;
            Stream? translatedImagesStream = null;
            Stream? finalDocStream = null;
            
            try
            {
                _logger.LogInformation("Processing image replacement for {FileName}", fileName);
                
                // Check if there's metadata for this file
                var metadataFileName = FileNamePatterns.GetImageMetadataFileName(fileName);
                
                List<ExtractedImage>? originalImageMetadata = null;
                try
                {
                    using var metadataStream = await _blobStorageService.DownloadFileFromContainerAsync(
                        metadataFileName, 
                        metadataContainerName, 
                        cancellationToken);
                        
                    using var reader = new StreamReader(metadataStream);
                    var metadataJson = await reader.ReadToEndAsync(cancellationToken);
                    originalImageMetadata = System.Text.Json.JsonSerializer.Deserialize<List<ExtractedImage>>(metadataJson);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("No image metadata found for {FileName}, skipping image replacement: {Error}", 
                        fileName, ex.Message);
                    continue;
                }
                
                if (originalImageMetadata == null || !originalImageMetadata.Any())
                {
                    _logger.LogInformation("No images to replace in {FileName}", fileName);
                    continue;
                }
                
                // Download the translated main document
                translatedDocStream = await _blobStorageService.DownloadFileFromContainerAsync(
                    fileName, 
                    targetContainerName, 
                    cancellationToken);
                
                // Download the translated images PDF
                var imagesFileName = FileNamePatterns.GetImagesPdfFileName(fileName);
                try
                {
                    translatedImagesStream = await _blobStorageService.DownloadFileFromContainerAsync(
                        imagesFileName, 
                        targetContainerName, 
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not find translated images PDF {ImagesFileName}: {Error}", 
                        imagesFileName, ex.Message);
                    continue;
                }
                
                // Perform the image replacement
                finalDocStream = await _imageReplacementService.ReplaceImagesInTranslatedDocumentAsync(
                    fileName,
                    translatedDocStream,
                    translatedImagesStream,
                    jobId,
                    cancellationToken);
                
                // Upload the final document back to the target container
                await _blobStorageService.UploadFileToContainerAsync(
                    finalDocStream, 
                    fileName, 
                    targetContainerName, 
                    cancellationToken);
                
                _logger.LogInformation("Successfully replaced images in {FileName} and uploaded final version", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image replacement for {FileName}, file will be available without image replacement", 
                    fileName);
            }
            finally
            {
                // Ensure all streams are disposed
                translatedDocStream?.Dispose();
                translatedImagesStream?.Dispose();
                finalDocStream?.Dispose();
            }
        }
        
        _logger.LogInformation("Completed post-translation image replacement processing for job {JobId}", jobId);
    }
}
