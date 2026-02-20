using DocTranslationV2.Models;

namespace DocTranslationV2.Services;

public interface IBlobStorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileAsync(string blobPath, CancellationToken cancellationToken = default);
    Task<bool> DeleteFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<bool> DeleteContainerAsync(string containerName, CancellationToken cancellationToken = default);
    Task<List<string>> ListFilesInFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<List<string>> ListFilesInContainerAsync(string containerName, CancellationToken cancellationToken = default);
    Task EnsureFolderExistsAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<string> UploadFileToContainerAsync(Stream fileStream, string fileName, string containerName, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileFromContainerAsync(string fileName, string containerName, CancellationToken cancellationToken = default);
}

public interface IDocumentTranslationService
{
    Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default);
    Task<TranslationResponse> TranslateDocumentsAsync(TranslationRequest request, CancellationToken cancellationToken = default);
    Task<JobStatus> GetTranslationStatusAsync(string jobId, CancellationToken cancellationToken = default);
    bool IsFileSupported(string fileName);
    bool IsFileSupportedForMode(string fileName, bool isAsync);
    bool SupportsImageProcessing(string fileName);
    
    // New methods for job management
    Task<List<TranslationJobInfo>> GetAllTranslationJobsAsync(CancellationToken cancellationToken = default);
    Task<bool> CancelTranslationJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<List<bool>> CancelTranslationJobsAsync(List<string> jobIds, CancellationToken cancellationToken = default);
}

public interface IImageExtractionService
{
    Task<DocumentImageInfo> ExtractImagesFromPdfAsync(Stream pdfStream, string fileName, ImageFilteringOptions? filteringOptions = null);
    Task<DocumentImageInfo> ExtractImagesFromWordAsync(Stream wordStream, string fileName, ImageFilteringOptions? filteringOptions = null);
    Task<DocumentImageInfo> ExtractImagesFromPowerPointAsync(Stream pptxStream, string fileName, ImageFilteringOptions? filteringOptions = null);
    Task<Stream> CreatePdfFromImagesAsync(List<ExtractedImage> images, string jobId);
    Task<Stream> ReplaceImagesInWordDocumentAsync(Stream originalWordStream, Stream translatedWordStream, List<ExtractedImage> translatedImages);
    Task<Stream> ReplaceImagesInPdfAsync(Stream originalPdfStream, Stream translatedPdfStream, List<ExtractedImage> translatedImages);
    Task<Stream> ReplaceImagesInPowerPointAsync(Stream originalPptxStream, Stream translatedPptxStream, List<ExtractedImage> translatedImages);
}

/// <summary>
/// Python-based PDF image replacement service
/// </summary>
public interface IPythonPdfService
{
    Task<Stream> ReplaceImagesInPdfAsync(
        Stream translatedPdfStream,
        List<ExtractedImage> imageMappings,
        CancellationToken cancellationToken = default);
}
