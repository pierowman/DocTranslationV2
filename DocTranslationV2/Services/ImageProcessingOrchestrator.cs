using Azure.AI.Translation.Document;
using DocTranslationV2.Models;
using DocTranslationV2.Constants;

namespace DocTranslationV2.Services;

/// <summary>
/// Orchestrates the complete image processing pipeline for document translation
/// </summary>
public class ImageProcessingOrchestrator : IImageProcessingOrchestrator
{
    private readonly IImageExtractionService _imageExtraction;
    private readonly IImageReplacementService _imageReplacement;
    private readonly IBlobStorageService _blobStorage;
    private readonly IJobManagementService _jobManagement;
    private readonly ITranslationOperationService _translationOps;
    private readonly ILogger<ImageProcessingOrchestrator> _logger;

    public ImageProcessingOrchestrator(
        IImageExtractionService imageExtraction,
        IImageReplacementService imageReplacement,
        IBlobStorageService blobStorage,
        IJobManagementService jobManagement,
        ITranslationOperationService translationOps,
        ILogger<ImageProcessingOrchestrator> logger)
    {
        _imageExtraction = imageExtraction;
        _imageReplacement = imageReplacement;
        _blobStorage = blobStorage;
        _jobManagement = jobManagement;
        _translationOps = translationOps;
        _logger = logger;
    }

    public async Task ProcessImageExtractionAsync(
        List<IFormFile> files,
        string containerName,
        string jobId,
        ImageFilteringOptions? filteringOptions,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting image extraction for job {JobId} with {FileCount} files",
            jobId, files.Count);

        var semaphore = new SemaphoreSlim(4);
        var tasks = new List<Task>();

        foreach (var file in files)
        {
            var fileName = file.FileName;
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (!SupportsImageProcessing(fileName))
            {
                continue;
            }

            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    await ProcessSingleFileImageExtractionAsync(
                        file, fileName, containerName, extension, filteringOptions, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation("Completed image extraction for job {JobId}", jobId);
    }

    public async Task ProcessImageReplacementAsync(
        List<IFormFile> originalFiles,
        string targetContainerName,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting image replacement for job {JobId} in container {Container}",
            jobId, targetContainerName);

        var metadataContainerName = ContainerNamePatterns.GetMetadataContainerName(jobId);

        foreach (var file in originalFiles)
        {
            var fileName = file.FileName;
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (!SupportsImageProcessing(fileName))
            {
                continue;
            }

            Stream? translatedDocStream = null;
            Stream? translatedImagesStream = null;
            Stream? finalDocStream = null;

            try
            {
                _logger.LogInformation("Processing image replacement for {FileName}", fileName);

                // Check for metadata
                var metadataFileName = FileNamePatterns.GetImageMetadataFileName(fileName);
                List<ExtractedImage>? originalImageMetadata = null;

                try
                {
                    using var metadataStream = await _blobStorage.DownloadFileFromContainerAsync(
                        metadataFileName, metadataContainerName, cancellationToken);

                    using var reader = new StreamReader(metadataStream);
                    var metadataJson = await reader.ReadToEndAsync(cancellationToken);
                    originalImageMetadata = System.Text.Json.JsonSerializer.Deserialize<List<ExtractedImage>>(metadataJson);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("No image metadata found for {FileName}: {Error}",
                        fileName, ex.Message);
                    continue;
                }

                if (originalImageMetadata == null || !originalImageMetadata.Any())
                {
                    _logger.LogInformation("No images to replace in {FileName}", fileName);
                    continue;
                }

                // Download translated document
                translatedDocStream = await _blobStorage.DownloadFileFromContainerAsync(
                    fileName, targetContainerName, cancellationToken);

                // Download translated images PDF
                var imagesFileName = FileNamePatterns.GetImagesPdfFileName(fileName);
                try
                {
                    translatedImagesStream = await _blobStorage.DownloadFileFromContainerAsync(
                        imagesFileName, targetContainerName, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not find translated images PDF {ImagesFile}: {Error}",
                        imagesFileName, ex.Message);
                    continue;
                }

                // Replace images
                finalDocStream = await _imageReplacement.ReplaceImagesInTranslatedDocumentAsync(
                    fileName, translatedDocStream, translatedImagesStream, jobId, cancellationToken);

                // Upload final document
                await _blobStorage.UploadFileToContainerAsync(
                    finalDocStream, fileName, targetContainerName, cancellationToken);

                _logger.LogInformation("Successfully replaced images in {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image replacement for {FileName}", fileName);
            }
            finally
            {
                translatedDocStream?.Dispose();
                translatedImagesStream?.Dispose();
                finalDocStream?.Dispose();
            }
        }

        _logger.LogInformation("Completed image replacement for job {JobId}", jobId);
    }

    public async Task MonitorAndProcessImagesAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting unified background monitoring for job {JobId}", jobId);

            var metadata = _jobManagement.GetJobMetadata(jobId);
            if (metadata == null)
            {
                _logger.LogError("Job metadata not found for {JobId}", jobId);
                return;
            }

            var operationIds = new List<string>(metadata.AllOperationIds);
            var originalFiles = metadata.OriginalFiles;
            var containersByLanguage = new Dictionary<string, string>(metadata.TargetContainersByLanguage);
            var operationToLanguage = new Dictionary<string, string>(metadata.OperationIdToLanguage);

            _logger.LogInformation("Monitoring {Count} operations for job {JobId}", operationIds.Count, jobId);

            // Wait for all operations to complete
            var completedOperations = new Dictionary<string, DocumentTranslationStatus>();

            foreach (var operationId in operationIds)
            {
                var operation = _translationOps.GetCachedOperation(operationId);
                if (operation == null)
                {
                    _logger.LogError("Operation {OperationId} not found in cache for job {JobId}",
                        operationId, jobId);
                    completedOperations[operationId] = DocumentTranslationStatus.Failed;
                    continue;
                }

                try
                {
                    var languageCode = operationToLanguage.TryGetValue(operationId, out var lang) ? lang : "unknown";
                    _logger.LogInformation("Waiting for operation {OperationId} (language: {Language}) to complete",
                        operationId, languageCode);

                    var status = await _translationOps.WaitForCompletionAsync(operationId, cancellationToken);
                    completedOperations[operationId] = status;

                    _logger.LogInformation("Operation {OperationId} completed with status: {Status}",
                        operationId, status);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error waiting for operation {OperationId}", operationId);
                    completedOperations[operationId] = DocumentTranslationStatus.Failed;
                }
            }

            // Check results
            var allSucceeded = completedOperations.Values.All(s => s == DocumentTranslationStatus.Succeeded);
            var anyFailed = completedOperations.Values.Any(s => s == DocumentTranslationStatus.Failed);

            if (allSucceeded)
            {
                _logger.LogInformation("All {Count} operations succeeded for job {JobId}, starting image replacement",
                    operationIds.Count, jobId);

                _jobManagement.UpdateJobPhase(jobId, JobPhases.ReplacingImages);

                // Check if any files support image processing
                var filesWithImageSupport = originalFiles
                    .Where(f => SupportsImageProcessing(f.FileName))
                    .ToList();

                if (!filesWithImageSupport.Any())
                {
                    _logger.LogInformation("No files in job {JobId} support image processing, skipping image replacement phase", jobId);
                }
                else
                {
                    _logger.LogInformation("Processing image replacement for {FileCount} files that support images", 
                        filesWithImageSupport.Count);

                    // Process image replacement for each target container
                    foreach (var kvp in containersByLanguage)
                    {
                        var language = kvp.Key;
                        var targetContainerName = kvp.Value;

                        try
                        {
                            _logger.LogInformation("Processing image replacement for language {Language} in container {Container}",
                                language, targetContainerName);

                            await ProcessImageReplacementAsync(
                                originalFiles, targetContainerName, jobId, cancellationToken);

                            _logger.LogInformation("Image replacement completed for language {Language}", language);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing image replacement for language {Language}", language);
                            // Don't fail the entire job if image replacement fails - just log it
                        }
                    }
                }

                _logger.LogInformation("Job {JobId} completed successfully", jobId);
                _jobManagement.CompleteJob(jobId, success: true);
            }
            else if (anyFailed)
            {
                _logger.LogWarning("Some operations failed for job {JobId}", jobId);
                _jobManagement.CompleteJob(jobId, success: false, errorMessage: "One or more translation operations failed");
            }
            else
            {
                _logger.LogWarning("Translations completed with mixed status for job {JobId}", jobId);
                _jobManagement.CompleteJob(jobId, success: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in unified background monitoring for job {JobId}", jobId);
            _jobManagement.CompleteJob(jobId, success: false, errorMessage: ex.Message);
        }
    }

    public bool SupportsImageProcessing(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pdf" || extension == ".docx" || extension == ".pptx";
    }

    private async Task ProcessSingleFileImageExtractionAsync(
        IFormFile file,
        string fileName,
        string containerName,
        string extension,
        ImageFilteringOptions? filteringOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Extracting images from {FileName}", fileName);

            using var downloadStream = await _blobStorage.DownloadFileFromContainerAsync(
                fileName, containerName, cancellationToken);

            DocumentImageInfo imageInfo;
            
            if (extension == ".pdf")
            {
                imageInfo = await _imageExtraction.ExtractImagesFromPdfAsync(downloadStream, fileName, filteringOptions);
            }
            else if (extension == ".docx")
            {
                imageInfo = await _imageExtraction.ExtractImagesFromWordAsync(downloadStream, fileName, filteringOptions);
            }
            else if (extension == ".pptx")
            {
                imageInfo = await _imageExtraction.ExtractImagesFromPowerPointAsync(downloadStream, fileName, filteringOptions);
            }
            else
            {
                _logger.LogWarning("Unsupported file extension {Extension} for image extraction", extension);
                return;
            }

            if (imageInfo.HasImages && imageInfo.HasTextContent)
            {
                _logger.LogInformation("Creating images PDF for {FileName} with {Count} images",
                    fileName, imageInfo.Images.Count);

                // Extract jobId from containerName (format: job-{jobId}-source)
                var jobId = containerName.Replace("job-", "").Replace("-source", "");
                
                using var imagesPdfStream = await _imageExtraction.CreatePdfFromImagesAsync(imageInfo.Images, jobId);
                var imagesPdfName = FileNamePatterns.GetImagesPdfFileName(fileName);
                await _blobStorage.UploadFileToContainerAsync(
                    imagesPdfStream, imagesPdfName, containerName, cancellationToken);

                // Save metadata
                var metadataContainerName = $"{containerName}-metadata";
                var metadataFileName = FileNamePatterns.GetImageMetadataFileName(fileName);
                var metadataJson = System.Text.Json.JsonSerializer.Serialize(imageInfo.Images);
                using var metadataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(metadataJson));
                await _blobStorage.UploadFileToContainerAsync(
                    metadataStream, metadataFileName, metadataContainerName, cancellationToken);

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
            _logger.LogError(ex, "Error processing images for {FileName}", fileName);
        }
    }
}
