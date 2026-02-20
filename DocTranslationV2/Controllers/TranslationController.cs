using DocTranslationV2.Models;
using DocTranslationV2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DocTranslationV2.Controllers;

public class TranslationController : Controller
{
    private readonly IDocumentTranslationService _translationService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IImageReplacementService _imageReplacementService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<TranslationController> _logger;
    private readonly TranslationConfiguration _config;

    public TranslationController(
        IDocumentTranslationService translationService,
        IBlobStorageService blobStorageService,
        IImageReplacementService imageReplacementService,
        IMemoryCache memoryCache,
        ILogger<TranslationController> logger,
        IOptions<TranslationConfiguration> config)
    {
        _translationService = translationService;
        _blobStorageService = blobStorageService;
        _imageReplacementService = imageReplacementService;
        _memoryCache = memoryCache;
        _logger = logger;
        _config = config.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try
        {
            var languages = await _translationService.GetSupportedLanguagesAsync();
            ViewBag.SupportedLanguages = languages;
            ViewBag.SupportedExtensions = FileValidationHelper.GetSupportedExtensionsString();
            ViewBag.MaxFileSize = FileValidationHelper.FormatBytes(524288000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading translation page");
            ViewBag.SupportedLanguages = new List<SupportedLanguage>();
        }
        
        return View();
    }

    [HttpGet]
    public IActionResult Jobs()
    {
        return View();
    }

    [HttpPost]
    [RequestSizeLimit(524288000)] // 500 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 524288000)]
    public async Task<IActionResult> Translate([FromForm] TranslationRequestViewModel model)
    {
        try
        {
            _logger.LogInformation("Translation request received with {FileCount} files", model.Files?.Count ?? 0);

            if (model.Files == null || !model.Files.Any())
            {
                return BadRequest(new { error = "No files uploaded" });
            }

            // Validate all files before processing
            foreach (var file in model.Files)
            {
                var (isValid, errorMessage) = FileValidationHelper.ValidateFile(
                    file.FileName, 
                    file.Length, 
                    !model.UseAsyncProcessing);

                if (!isValid)
                {
                    return BadRequest(new { error = errorMessage });
                }

                // Additional check using translation service
                if (!_translationService.IsFileSupported(file.FileName))
                {
                    return BadRequest(new { error = $"File type not supported by translation service: {file.FileName}" });
                }
            }

            // Merge UI filtering options with config defaults
            ImageFilteringOptions? imageFiltering = null;
            if (model.ProcessImages)
            {
                imageFiltering = new ImageFilteringOptions
                {
                    FilterImagesWithContainedText = model.FilterImagesWithContainedText,
                    FilterDecorativeImages = model.FilterDecorativeImages,
                    MinimumImageSizeBytes = _config.ImageFiltering.MinimumImageSizeBytes,
                    MinimumImageWidthPixels = _config.ImageFiltering.MinimumImageWidthPixels,
                    MinimumImageHeightPixels = _config.ImageFiltering.MinimumImageHeightPixels
                };
                
                _logger.LogInformation("Image filtering options - Text filter: {TextFilter}, Decorative filter: {DecorativeFilter}",
                    imageFiltering.FilterImagesWithContainedText, imageFiltering.FilterDecorativeImages);
            }

            var request = new TranslationRequest
            {
                Files = model.Files,
                SourceLanguage = model.SourceLanguage,
                TargetLanguages = model.TargetLanguages ?? new List<string>(),
                UseAsyncProcessing = model.UseAsyncProcessing,
                AutoDetectLanguage = model.AutoDetectLanguage,
                ProcessImages = model.ProcessImages,
                ImageFiltering = imageFiltering
            };

            var response = await _translationService.TranslateDocumentsAsync(request);

            _logger.LogInformation("Translation job {JobId} started with status {Status}", 
                response.JobId, response.Status);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing translation request");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus(string jobId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return BadRequest(new { error = "Job ID is required" });
            }

            var status = await _translationService.GetTranslationStatusAsync(jobId);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting translation status for job {JobId}", jobId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DownloadFile([FromBody] DownloadRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.BlobPath))
            {
                return BadRequest(new { error = "Blob path is required" });
            }

            _logger.LogInformation("Download request for {BlobPath}", request.BlobPath);

            // Sync translations are cached in memory — serve directly without hitting blob storage
            if (request.BlobPath.StartsWith("sync:", StringComparison.Ordinal))
            {
                if (!_memoryCache.TryGetValue(request.BlobPath, out byte[]? cachedBytes) || cachedBytes is null)
                {
                    return NotFound(new { error = "Translated file no longer available. Please re-translate the document." });
                }

                var fileName = request.BlobPath.Split(':').Last();
                return File(cachedBytes, "application/octet-stream", fileName);
            }

            // Image replacement now happens immediately after translation completes,
            // so we just download and return the file that already has images replaced
            var fileStream = await _blobStorageService.DownloadFileAsync(request.BlobPath);
            var blobFileName = Path.GetFileName(request.BlobPath);

            return File(fileStream, "application/octet-stream", blobFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {BlobPath}", request.BlobPath);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CleanupJob([FromBody] CleanupRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JobId))
            {
                return BadRequest(new { error = "Job ID is required" });
            }

            _logger.LogInformation("Cleanup request for job {JobId}", request.JobId);

            // For batch translations, we use separate containers per job
            // Container names: job-{jobId}-source, job-{jobId}-target, and job-{jobId}-source-metadata
            var sourceContainerName = $"job-{request.JobId}-source";
            var targetContainerName = $"job-{request.JobId}-target";
            var metadataContainerName = $"job-{request.JobId}-source-metadata";

            // Try deleting containers first (batch translation)
            var sourceDeleted = await _blobStorageService.DeleteContainerAsync(sourceContainerName);
            var targetDeleted = await _blobStorageService.DeleteContainerAsync(targetContainerName);
            var metadataDeleted = await _blobStorageService.DeleteContainerAsync(metadataContainerName);

            // Also try folder-based cleanup (sync translation fallback)
            if (!sourceDeleted || !targetDeleted)
            {
                _logger.LogInformation("Container deletion failed or not found, trying folder-based cleanup for job {JobId}", request.JobId);
                var sourcePath = $"jobs/{request.JobId}/source";
                var targetPath = $"jobs/{request.JobId}/target";
                sourceDeleted = await _blobStorageService.DeleteFolderAsync(sourcePath);
                targetDeleted = await _blobStorageService.DeleteFolderAsync(targetPath);
            }

            if (sourceDeleted && targetDeleted)
            {
                // Metadata container deletion is optional - log if it fails but don't fail the operation
                if (!metadataDeleted)
                {
                    _logger.LogWarning("Metadata container {Container} could not be deleted for job {JobId}", 
                        metadataContainerName, request.JobId);
                }
                return Ok(new { message = "Cleanup completed successfully" });
            }
            else
            {
                return StatusCode(500, new { error = "Partial cleanup failure" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up job {JobId}", request.JobId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult> GetTranslatedFiles(string jobId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return BadRequest(new { error = "Job ID is required" });
            }

            _logger.LogInformation("Getting translated files for job {JobId}", jobId);

            // Get the job status to retrieve target languages
            var jobStatus = await _translationService.GetTranslationStatusAsync(jobId);
            var targetLanguages = jobStatus.TargetLanguages ?? new List<string>();
            
            if (targetLanguages.Count == 0)
            {
                _logger.LogWarning("No target languages found in job metadata for {JobId}", jobId);
            }

            // Get supported languages for name lookup
            var languages = await _translationService.GetSupportedLanguagesAsync();
            var languageLookup = languages.ToDictionary(l => l.Code, l => l.Name);

            var translatedFiles = new List<object>();

            // For each target language, check its dedicated container
            foreach (var targetLanguage in targetLanguages)
            {
                // Azure Blob Storage container names must be lowercase
                var targetContainerName = $"job-{jobId}-target-{targetLanguage.ToLowerInvariant()}";
                
                _logger.LogInformation("Checking container {Container} for language {Language}", 
                    targetContainerName, targetLanguage);
                
                var files = await _blobStorageService.ListFilesInContainerAsync(targetContainerName);
                
                if (files.Count > 0)
                {
                    _logger.LogInformation("Found {Count} files in container {Container}", 
                        files.Count, targetContainerName);
                    
                    var languageName = languageLookup.TryGetValue(targetLanguage, out var name) ? name : targetLanguage;
                    
                    // Add each file with its correct language
                    foreach (var file in files.Where(f => !f.EndsWith("_images.pdf") && !f.Contains("_image_metadata.json")))
                    {
                        var fileName = Path.GetFileName(file);
                        var filePath = $"{targetContainerName}/{fileName}";
                        
                        translatedFiles.Add(new
                        {
                            path = filePath,
                            name = fileName,
                            language = targetLanguage,
                            languageName = languageName,
                            category = FileValidationHelper.GetFileCategory(fileName)
                        });
                    }
                }
            }

            // If no files found in language-specific containers, try legacy single container
            if (translatedFiles.Count == 0)
            {
                _logger.LogInformation("No files found in language-specific containers, trying legacy single container");
                
                var targetContainerName = $"job-{jobId}-target";
                var files = await _blobStorageService.ListFilesInContainerAsync(targetContainerName);
                bool isContainerBased = files.Count > 0;
                
                // If no files in container, try folder-based storage (sync translation fallback)
                if (files.Count == 0)
                {
                    _logger.LogInformation("No files found in container {Container}, trying folder-based storage", targetContainerName);
                    var targetPath = $"jobs/{jobId}/target";
                    files = await _blobStorageService.ListFilesInFolderAsync(targetPath);
                }
                
                if (files.Count > 0)
                {
                    translatedFiles.AddRange(files
                        .Where(f => !f.EndsWith("_images.pdf") && !f.Contains("_image_metadata.json"))
                        .Select(f =>
                        {
                            var fileName = Path.GetFileName(f);
                            
                            string languageCode;
                            if (isContainerBased && targetLanguages.Count > 0)
                            {
                                languageCode = targetLanguages[0];
                            }
                            else
                            {
                                languageCode = ExtractLanguageFromPath(f);
                            }
                            
                            var languageName = languageLookup.TryGetValue(languageCode, out var name) ? name : languageCode;
                            var filePath = f.Contains('/') ? f : $"{targetContainerName}/{fileName}";
                            
                            return new
                            {
                                path = filePath,
                                name = fileName,
                                language = languageCode,
                                languageName = languageName,
                                category = FileValidationHelper.GetFileCategory(fileName)
                            };
                        }));
                }
            }
            
            if (translatedFiles.Count == 0)
            {
                _logger.LogWarning("No translated files found for job {JobId}", jobId);
                return Ok(new List<object>());
            }

            _logger.LogInformation("Returning {Count} translated files for job {JobId} across {LanguageCount} language(s)", 
                translatedFiles.Count, jobId, targetLanguages.Count);
            return Ok(translatedFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting translated files for job {JobId}", jobId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult GetSupportedLanguages()
    {
        try
        {
            var languages = _translationService.GetSupportedLanguagesAsync().GetAwaiter().GetResult();
            return Ok(languages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting supported languages");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult GetSupportedFileTypes()
    {
        try
        {
            // Get configuration from appsettings
            var config = HttpContext.RequestServices.GetRequiredService<IOptions<TranslationConfiguration>>();
            var fileTypes = config.Value.AzureTranslation.SupportedFileTypes;

            var response = new
            {
                batch = fileTypes.Batch,
                sync = fileTypes.Sync,
                imageProcessingSupported = fileTypes.ImageProcessingSupported
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting supported file types");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllJobs()
    {
        try
        {
            _logger.LogInformation("Fetching all translation jobs");
            var jobs = await _translationService.GetAllTranslationJobsAsync();
            return Ok(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all translation jobs");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CancelJob([FromBody] CancelJobRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.JobId))
            {
                return BadRequest(new { error = "Job ID is required" });
            }

            _logger.LogInformation("Canceling translation job {JobId}", request.JobId);
            var result = await _translationService.CancelTranslationJobAsync(request.JobId);

            if (result)
            {
                return Ok(new { message = "Job canceled successfully" });
            }
            else
            {
                return BadRequest(new { error = "Job could not be canceled (may already be completed)" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling job {JobId}", request.JobId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CancelJobs([FromBody] CancelJobsRequest request)
    {
        try
        {
            if (request.JobIds == null || request.JobIds.Count == 0)
            {
                return BadRequest(new { error = "Job IDs are required" });
            }

            _logger.LogInformation("Canceling {JobCount} translation jobs", request.JobIds.Count);
            var results = await _translationService.CancelTranslationJobsAsync(request.JobIds);

            var successCount = results.Count(r => r);
            var failCount = results.Count(r => !r);

            return Ok(new 
            { 
                message = $"Canceled {successCount} job(s), {failCount} failed",
                successCount,
                failCount,
                details = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling multiple jobs");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static string ExtractLanguageFromPath(string path)
    {
        var parts = path.Split('/');
        return parts.Length > 3 ? parts[3] : "unknown";
    }
}

public class DownloadRequest
{
    public string BlobPath { get; set; } = string.Empty;
    public bool ApplyImageReplacement { get; set; } = false; // Default to false - only apply when explicitly requested
    public string JobId { get; set; } = string.Empty;
}

public class CleanupRequest
{
    public string JobId { get; set; } = string.Empty;
}

public class CancelJobRequest
{
    public string JobId { get; set; } = string.Empty;
}

public class CancelJobsRequest
{
    public List<string> JobIds { get; set; } = [];
}
