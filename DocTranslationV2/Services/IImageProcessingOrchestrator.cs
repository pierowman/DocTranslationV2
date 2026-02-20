using DocTranslationV2.Models;

namespace DocTranslationV2.Services;

/// <summary>
/// Orchestrates the complete image processing pipeline for document translation
/// </summary>
public interface IImageProcessingOrchestrator
{
    /// <summary>
    /// Extracts images from documents and uploads them for translation
    /// </summary>
    Task ProcessImageExtractionAsync(
        List<IFormFile> files,
        string containerName,
        string jobId,
        ImageFilteringOptions? filteringOptions,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Replaces translated images back into translated documents
    /// </summary>
    Task ProcessImageReplacementAsync(
        List<IFormFile> originalFiles,
        string targetContainerName,
        string jobId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Monitors translation operations and triggers image replacement when complete
    /// </summary>
    Task MonitorAndProcessImagesAsync(
        string jobId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a file supports image processing
    /// </summary>
    bool SupportsImageProcessing(string fileName);
}
