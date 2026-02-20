using DocTranslationV2.Models;
using DocTranslationV2.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace DocTranslationV2.Services;

/// <summary>
/// Orchestrates document translation by coordinating specialized services.
/// This is a thin orchestration layer that delegates to focused services.
/// </summary>
public class DocumentTranslationServiceV2 : IDocumentTranslationService
{
    private readonly IJobManagementService _jobManagement;
    private readonly ITranslationOperationService _translationOps;
    private readonly IStatusTrackingService _statusTracking;
    private readonly IContainerManagementService _containerManagement;
    private readonly IImageProcessingOrchestrator _imageProcessing;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILanguageService _languageService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DocumentTranslationServiceV2> _logger;
    private readonly AzureTranslationSettings _settings;

    private const string SyncCachePrefix = "sync:";

    public DocumentTranslationServiceV2(
        IJobManagementService jobManagement,
        ITranslationOperationService translationOps,
        IStatusTrackingService statusTracking,
        IContainerManagementService containerManagement,
        IImageProcessingOrchestrator imageProcessing,
        IBlobStorageService blobStorage,
        ILanguageService languageService,
        IMemoryCache memoryCache,
        ILogger<DocumentTranslationServiceV2> logger,
        Microsoft.Extensions.Options.IOptions<TranslationConfiguration> config)
    {
        _jobManagement = jobManagement;
        _translationOps = translationOps;
        _statusTracking = statusTracking;
        _containerManagement = containerManagement;
        _imageProcessing = imageProcessing;
        _blobStorage = blobStorage;
        _languageService = languageService;
        _memoryCache = memoryCache;
        _logger = logger;
        _settings = config.Value.AzureTranslation;
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
        return _imageProcessing.SupportsImageProcessing(fileName);
    }

    public async Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching supported languages from Language Service");
            var languages = await _languageService.GetSupportedLanguagesAsync(cancellationToken);
            _logger.LogInformation("Retrieved {Count} supported languages", languages.Count);
            return languages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching supported languages");
            throw;
        }
    }

    public async Task<TranslationResponse> TranslateDocumentsAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new TranslationResponse { IsAsync = request.UseAsyncProcessing };

        try
        {
            _logger.LogInformation("Starting translation with {FileCount} files to {LanguageCount} language(s)",
                request.Files.Count, request.TargetLanguages.Count);

            // Validate files
            ValidateFiles(request);

            // Force async for multiple files
            if (request.Files.Count > 1 && !request.UseAsyncProcessing)
            {
                _logger.LogWarning("Multiple files detected, forcing async processing");
                request.UseAsyncProcessing = true;
                response.IsAsync = true;
            }

            if (request.UseAsyncProcessing)
            {
                return await ProcessBatchTranslationAsync(request, cancellationToken);
            }
            else
            {
                return await ProcessSynchronousTranslationAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting translation");
            response.Status = TranslationStatus.Failed;
            response.ErrorMessage = ex.Message;
            return response;
        }
    }

    public async Task<JobStatus> GetTranslationStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        return await _statusTracking.GetJobStatusAsync(jobId, cancellationToken);
    }

    public async Task<List<TranslationJobInfo>> GetAllTranslationJobsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching all translation jobs");

            var jobs = new List<TranslationJobInfo>();

            await foreach (var statusResponse in _translationOps.GetAllOperationsAsync(cancellationToken))
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

    public async Task<bool> CancelTranslationJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Canceling translation job {JobId}", jobId);

            var metadata = _jobManagement.GetJobMetadata(jobId);
            if (metadata == null)
            {
                _logger.LogWarning("Job {JobId} not found", jobId);
                return false;
            }

            var results = new List<bool>();
            foreach (var operationId in metadata.AllOperationIds)
            {
                var result = await _translationOps.CancelOperationAsync(operationId, cancellationToken);
                results.Add(result);
            }

            var success = results.Any(r => r);
            if (success)
            {
                _jobManagement.CompleteJob(jobId, false, "Job cancelled by user");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling job {JobId}", jobId);
            throw;
        }
    }

    public async Task<List<bool>> CancelTranslationJobsAsync(
        List<string> jobIds,
        CancellationToken cancellationToken = default)
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

    // Private helper methods

    private void ValidateFiles(TranslationRequest request)
    {
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
            var unsupportedFiles = request.Files
                .Where(f => !SupportsImageProcessing(f.FileName))
                .Select(f => f.FileName)
                .ToList();

            if (unsupportedFiles.Any())
            {
                _logger.LogInformation("Image processing enabled but {Count} file(s) don't support it: {Files}",
                    unsupportedFiles.Count, string.Join(", ", unsupportedFiles));
            }
        }
    }

    private async Task<TranslationResponse> ProcessBatchTranslationAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting BATCH translation for {FileCount} files to {LanguageCount} language(s)",
            request.Files.Count, request.TargetLanguages.Count);

        // Create job
        var jobId = _jobManagement.CreateJob(new TranslationJobRequest
        {
            Files = request.Files,
            TargetLanguages = request.TargetLanguages,
            SourceLanguage = request.SourceLanguage,
            ProcessImages = request.ProcessImages,
            AutoDetectLanguage = request.AutoDetectLanguage,
            ImageFiltering = request.ImageFiltering
        });

        // Create source container
        var sourceContainerName = ContainerNamePatterns.GetSourceContainerName(jobId);
        await _containerManagement.CleanupExistingContainersIfNeededAsync(
            sourceContainerName, sourceContainerName, cancellationToken);

        await _containerManagement.CreateJobContainerAsync(sourceContainerName, cancellationToken);

        // Upload files
        _jobManagement.UpdateJobPhase(jobId, JobPhases.UploadingFiles);
        await UploadFilesAsync(request.Files, sourceContainerName, cancellationToken);

        // Extract images if enabled
        if (request.ProcessImages)
        {
            _jobManagement.UpdateJobPhase(jobId, JobPhases.ExtractingImages);
            await _imageProcessing.ProcessImageExtractionAsync(
                request.Files,
                sourceContainerName,
                jobId,
                request.ImageFiltering,
                cancellationToken);
        }

        // Start translations for each target language
        _jobManagement.UpdateJobPhase(jobId, JobPhases.StartingTranslation);

        var sourceUri = _containerManagement.GetContainerUri(sourceContainerName).ToString();

        foreach (var targetLanguage in request.TargetLanguages)
        {
            var targetContainerName = ContainerNamePatterns.GetTargetContainerName(jobId, targetLanguage);
            await _containerManagement.CreateJobContainerAsync(targetContainerName, cancellationToken);

            var targetUri = _containerManagement.GetContainerUri(targetContainerName).ToString();

            _logger.LogInformation("Starting translation to {Language}", targetLanguage);

            var operationId = await _translationOps.StartBatchTranslationAsync(
                sourceUri,
                targetUri,
                targetLanguage,
                request.SourceLanguage,
                request.AutoDetectLanguage,
                cancellationToken);

            _jobManagement.RegisterOperation(jobId, operationId, targetLanguage, targetContainerName);
        }

        // Update phase to translating
        _jobManagement.UpdateJobPhase(jobId, JobPhases.TranslatingDocuments);

        // Start background monitoring if image processing enabled
        if (request.ProcessImages)
        {
            _logger.LogInformation("Starting background monitoring for job {JobId}", jobId);
            _ = Task.Run(() => _imageProcessing.MonitorAndProcessImagesAsync(jobId, CancellationToken.None));
        }

        var metadata = _jobManagement.GetJobMetadata(jobId);

        return new TranslationResponse
        {
            JobId = jobId,
            Status = TranslationStatus.InProgress,
            IsAsync = true,
            CurrentPhase = metadata?.CurrentPhase ?? JobPhases.Initializing
        };
    }

    private async Task<TranslationResponse> ProcessSynchronousTranslationAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Files.Count != 1)
        {
            throw new InvalidOperationException("Synchronous translation requires exactly one file");
        }

        var file = request.Files[0];
        var fileName = file.FileName;
        var jobId = Guid.NewGuid().ToString();

        _logger.LogInformation("Starting SYNC translation for {FileName} to {LanguageCount} language(s)",
            fileName, request.TargetLanguages.Count);

        var response = new TranslationResponse
        {
            JobId = jobId,
            Status = TranslationStatus.Succeeded,
            IsAsync = false
        };

        try
        {
            foreach (var targetLang in request.TargetLanguages)
            {
                _logger.LogInformation("Translating {FileName} to {TargetLanguage}", fileName, targetLang);

                using var fileStream = file.OpenReadStream();
                using var translatedStream = await _translationOps.TranslateSingleDocumentAsync(
                    fileStream,
                    fileName,
                    targetLang,
                    request.SourceLanguage,
                    request.AutoDetectLanguage,
                    cancellationToken);

                // Cache translated content in memory — no blob upload needed for sync
                var translatedBytes = new MemoryStream();
                await translatedStream.CopyToAsync(translatedBytes, cancellationToken);
                var cacheKey = $"{SyncCachePrefix}{jobId}:{targetLang}:{fileName}";
                _memoryCache.Set(cacheKey, translatedBytes.ToArray(), TimeSpan.FromMinutes(30));

                // Get language name
                var languages = await _languageService.GetSupportedLanguagesAsync(cancellationToken);
                var language = languages.FirstOrDefault(l => l.Code == targetLang);
                var languageName = language?.Name ?? targetLang;

                response.TranslatedFiles.Add(new TranslatedFile
                {
                    OriginalFileName = fileName,
                    TargetLanguage = targetLang,
                    TargetLanguageName = languageName,
                    TranslatedBlobUrl = cacheKey
                });
            }

            _logger.LogInformation("Synchronous translation completed successfully for job {JobId}", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in synchronous translation for {FileName}", fileName);
            response.Status = TranslationStatus.Failed;
            response.ErrorMessage = ex.Message;
        }

        return response;
    }

    private async Task UploadFilesAsync(
        List<IFormFile> files,
        string containerName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Uploading {FileCount} files to container {Container}",
            files.Count, containerName);

        var semaphore = new SemaphoreSlim(4); // Process 4 files concurrently
        var uploadTasks = new List<Task>();

        foreach (var file in files)
        {
            await semaphore.WaitAsync(cancellationToken);

            var uploadTask = Task.Run(async () =>
            {
                try
                {
                    using var stream = file.OpenReadStream();
                    await _blobStorage.UploadFileToContainerAsync(
                        stream,
                        file.FileName,
                        containerName,
                        cancellationToken);

                    _logger.LogInformation("Uploaded {FileName} to {Container}",
                        file.FileName, containerName);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            uploadTasks.Add(uploadTask);
        }

        await Task.WhenAll(uploadTasks);

        _logger.LogInformation("All files uploaded to container {Container}", containerName);
    }
}
