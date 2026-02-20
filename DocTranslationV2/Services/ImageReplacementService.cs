using DocTranslationV2.Models;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using PdfReader = iText.Kernel.Pdf.PdfReader;
using PdfDocument = iText.Kernel.Pdf.PdfDocument;

namespace DocTranslationV2.Services;

/// <summary>
/// Service responsible for post-translation image replacement workflow
/// </summary>
public interface IImageReplacementService
{
    Task<Stream> ReplaceImagesInTranslatedDocumentAsync(
        string originalFileName,
        Stream translatedDocumentStream,
        Stream translatedImagesPdfStream,
        string jobId,
        CancellationToken cancellationToken = default);
}

public class ImageReplacementService : IImageReplacementService
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IImageExtractionService _imageExtractionService;
    private readonly ILogger<ImageReplacementService> _logger;

    public ImageReplacementService(
        IBlobStorageService blobStorageService,
        IImageExtractionService imageExtractionService,
        ILogger<ImageReplacementService> logger)
    {
        _blobStorageService = blobStorageService;
        _imageExtractionService = imageExtractionService;
        _logger = logger;
    }

    public async Task<Stream> ReplaceImagesInTranslatedDocumentAsync(
        string originalFileName,
        Stream translatedDocumentStream,
        Stream translatedImagesPdfStream,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting image replacement process for {FileName}", originalFileName);

            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            
            // Load original image metadata from the metadata container
            var metadataFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}_image_metadata.json";
            
            // Metadata is stored in a separate container: job-{jobId}-source-metadata
            // Extract the jobId from the full jobId path (handle both container-based and folder-based formats)
            var jobIdOnly = jobId.Replace("job-", "").Replace("-source", "").Replace("-target", "");
            var metadataContainerName = $"job-{jobIdOnly}-source-metadata";
            
            List<ExtractedImage>? originalImageMetadata = null;
            try
            {
                _logger.LogInformation("Loading metadata from container {Container}, file {FileName}", 
                    metadataContainerName, metadataFileName);
                    
                var metadataStream = await _blobStorageService.DownloadFileFromContainerAsync(
                    metadataFileName, 
                    metadataContainerName, 
                    cancellationToken);
                    
                using var reader = new StreamReader(metadataStream);
                var metadataJson = await reader.ReadToEndAsync(cancellationToken);
                originalImageMetadata = JsonSerializer.Deserialize<List<ExtractedImage>>(metadataJson);
                
                if (originalImageMetadata != null)
                {
                    _logger.LogInformation("Loaded metadata for {ImageCount} images", originalImageMetadata.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Could not load image metadata for {FileName}: {Error}. " +
                    "This is expected if no images were extracted. Returning translated document as-is.", 
                    originalFileName, ex.Message);
                return translatedDocumentStream;
            }

            if (originalImageMetadata == null || !originalImageMetadata.Any())
            {
                _logger.LogInformation("No image metadata found, returning translated document as-is");
                return translatedDocumentStream;
            }

            // Extract translated images from the translated images PDF
            var translatedImages = await ExtractTranslatedImagesFromPdf(
                translatedImagesPdfStream, 
                originalImageMetadata);

            if (translatedImages.Count != originalImageMetadata.Count)
            {
                _logger.LogWarning(
                    "Image count mismatch: Expected {Expected}, got {Actual}. Proceeding with available images.",
                    originalImageMetadata.Count, translatedImages.Count);
            }

            // Replace images in the translated document based on extension
            Stream resultStream;
            
            if (extension == ".docx" || extension == ".doc")
            {
                // Get original document for reference from source container
                var sourceContainerName = $"job-{jobId}-source";
                var originalDocStream = await _blobStorageService.DownloadFileFromContainerAsync(originalFileName, sourceContainerName, cancellationToken);
                
                resultStream = await _imageExtractionService.ReplaceImagesInWordDocumentAsync(
                    originalDocStream,
                    translatedDocumentStream,
                    translatedImages);
                    
                _logger.LogInformation("Completed image replacement in Word document");
            }
            else if (extension == ".pptx" || extension == ".ppt")
            {
                // Get original document for reference from source container
                var sourceContainerName = $"job-{jobId}-source";
                var originalDocStream = await _blobStorageService.DownloadFileFromContainerAsync(originalFileName, sourceContainerName, cancellationToken);
                
                resultStream = await _imageExtractionService.ReplaceImagesInPowerPointAsync(
                    originalDocStream,
                    translatedDocumentStream,
                    translatedImages);
                    
                _logger.LogInformation("Completed image replacement in PowerPoint document");
            }
            else if (extension == ".pdf")
            {
                // Get original document for reference from source container
                var sourceContainerName = $"job-{jobId}-source";
                var originalDocStream = await _blobStorageService.DownloadFileFromContainerAsync(originalFileName, sourceContainerName, cancellationToken);
                
                resultStream = await _imageExtractionService.ReplaceImagesInPdfAsync(
                    originalDocStream,
                    translatedDocumentStream,
                    translatedImages);
                    
                _logger.LogInformation("Completed image replacement in PDF document");
            }
            else
            {
                _logger.LogInformation("File type {Extension} does not support image replacement", extension);
                resultStream = translatedDocumentStream;
            }

            return resultStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during image replacement for {FileName}. Returning translated document without image replacement.", 
                originalFileName);
            return translatedDocumentStream;
        }
    }

    private async Task<List<ExtractedImage>> ExtractTranslatedImagesFromPdf(
        Stream translatedImagesPdf,
        List<ExtractedImage> originalMetadata)
    {
        try
        {
            _logger.LogInformation("Rendering {PageCount} PDF pages as images (includes text overlays)", 
                originalMetadata.Count);

            var translatedImages = new List<ExtractedImage>();
            
            // Save the PDF stream to a temporary file (Docnet.Core requires file path)
            var tempPdfPath = Path.GetTempFileName();
            try
            {
                // Write stream to temp file ONCE at the beginning
                await using (var fileStream = File.Create(tempPdfPath))
                {
                    translatedImagesPdf.Position = 0;
                    await translatedImagesPdf.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
                } // Dispose and close the file stream completely

                // Small delay to ensure file system releases the handle (Windows specific)
                await Task.Delay(100);

                // Check each page for text content using iText7
                var pageTextStatus = new Dictionary<int, bool>();
                
                // Open PDF for text extraction (read-only)
                await using (var pdfFileStream = new FileStream(tempPdfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var pdfReader = new PdfReader(pdfFileStream))
                using (var pdfDoc = new PdfDocument(pdfReader))
                {
                    _logger.LogInformation("Checking {PageCount} pages for text content", pdfDoc.GetNumberOfPages());
                    
                    for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
                    {
                        var page = pdfDoc.GetPage(pageNum);
                        var text = PdfTextExtractor.GetTextFromPage(page);
                        bool hasText = !string.IsNullOrWhiteSpace(text);
                        
                        pageTextStatus[pageNum - 1] = hasText; // Store as 0-indexed
                        
                        if (hasText)
                        {
                            _logger.LogInformation("Page {PageNum} contains text (length: {TextLength}) - will replace image", 
                                pageNum, text.Length);
                        }
                        else
                        {
                            _logger.LogInformation("Page {PageNum} has no text - will keep original image", pageNum);
                        }
                    }
                } // Dispose all PDF resources

                // Small delay to ensure PDF reader releases all handles
                await Task.Delay(100);

                // Render each page (one image per page in the images PDF)
                using var library = Docnet.Core.DocLib.Instance;
                using var docReader = library.GetDocReader(tempPdfPath, new Docnet.Core.Models.PageDimensions(1080, 1920));
                
                var pageCount = docReader.GetPageCount();
                _logger.LogInformation("PDF has {PageCount} pages to render", pageCount);

                // Ensure we have metadata for each page
                if (originalMetadata.Count != pageCount)
                {
                    _logger.LogWarning("Metadata count ({MetadataCount}) doesn't match page count ({PageCount})", 
                        originalMetadata.Count, pageCount);
                }

                // Render each page (one image per page in the images PDF)
                for (int pageIndex = 0; pageIndex < Math.Min(pageCount, originalMetadata.Count); pageIndex++)
                {
                    try
                    {
                        var originalImage = originalMetadata[pageIndex];
                        
                        // Check if this page has text from the Azure Translation Service
                        bool hasText = pageTextStatus.GetValueOrDefault(pageIndex, true); // Default to true for safety
                        
                        if (!hasText)
                        {
                            _logger.LogInformation("Skipping rendering for page {PageIndex} (image {ImageId}) - no text detected in translation", 
                                pageIndex, originalImage.ImageId);
                            
                            // Add the original image data back (no replacement needed)
                            translatedImages.Add(new ExtractedImage
                            {
                                PageNumber = originalImage.PageNumber,
                                ImageIndex = originalImage.ImageIndex,
                                ImageName = originalImage.ImageName,
                                ImageData = originalImage.ImageData, // Keep original
                                Format = originalImage.Format,
                                ImageId = originalImage.ImageId,
                                RelationshipId = originalImage.RelationshipId,
                                OriginalSize = originalImage.OriginalSize,
                                Width = originalImage.Width,
                                Height = originalImage.Height,
                                Position = originalImage.Position,
                                HasText = false // No text, don't replace
                            });
                            continue;
                        }
                        
                        using var pageReader = docReader.GetPageReader(pageIndex);
                        
                        // GetPageWidth/Height return the RENDERED dimensions (after PageDimensions scaling)
                        // These are the actual dimensions of the image data returned by GetImage()
                        var renderWidth = pageReader.GetPageWidth();
                        var renderHeight = pageReader.GetPageHeight();

                        // Render page as raw bytes (BGRA format)
                        var rawBytes = pageReader.GetImage();
                        
                        // Verify dimensions match the byte array size
                        var bytesPerPixel = 4; // BGRA format
                        var expectedBytes = renderWidth * renderHeight * bytesPerPixel;
                        
                        if (expectedBytes != rawBytes.Length)
                        {
                            _logger.LogError("Dimension mismatch for page {PageIndex}: {Width}x{Height} expects {Expected} bytes, but got {Actual} bytes", 
                                pageIndex, renderWidth, renderHeight, expectedBytes, rawBytes.Length);
                            continue; // Skip this page
                        }
                        
                        _logger.LogInformation("Rendered page {PageIndex} at {Width}x{Height} ({Bytes} bytes)", 
                            pageIndex, renderWidth, renderHeight, rawBytes.Length);

                        // Get the original image's XObject dimensions (pixel dimensions)
                        // We need to resize to match these so the PDF's transformation matrix works correctly
                        var targetWidth = originalImage.Width;  // Original XObject width
                        var targetHeight = originalImage.Height; // Original XObject height
                        
                        _logger.LogInformation("Scaling rendered image from {RenderW}x{RenderH} to match original XObject {TargetW}x{TargetH}",
                            renderWidth, renderHeight, targetWidth, targetHeight);

                        // Convert BGRA to PNG and resize to match original XObject dimensions
                        var pngBytes = ConvertAndResizeBgraToPng(rawBytes, renderWidth, renderHeight, 
                            targetWidth, targetHeight);

                        // Preserve original metadata with rendered image data (scaled to match original XObject)
                        translatedImages.Add(new ExtractedImage
                        {
                            PageNumber = originalImage.PageNumber,
                            ImageIndex = originalImage.ImageIndex,
                            ImageName = originalImage.ImageName,
                            ImageData = pngBytes, // Rendered page image with text overlays, scaled to match original
                            Format = "png",
                            ImageId = originalImage.ImageId,
                            RelationshipId = originalImage.RelationshipId, // Critical for matching
                            OriginalSize = pngBytes.Length,
                            Width = targetWidth,  // Matches original XObject dimensions
                            Height = targetHeight,
                            Position = originalImage.Position, // Preserve original position
                            HasText = hasText // Mark whether this image has translatable text
                        });

                        _logger.LogDebug("Rendered page {PageIndex} for image {ImageId} (relationship: {RelId})", 
                            pageIndex, originalImage.ImageId, originalImage.RelationshipId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error rendering page {PageIndex}", pageIndex);
                        // Continue with other pages
                    }
                }

                var textCount = translatedImages.Count(img => img.HasText);
                var noTextCount = translatedImages.Count(img => !img.HasText);
                
                _logger.LogInformation("Successfully processed {Total} images: {WithText} with text (will replace), {NoText} without text (keeping original)", 
                    translatedImages.Count, textCount, noTextCount);
                return translatedImages;
            }
            finally
            {
                // Clean up temp file with retry logic
                try
                {
                    if (File.Exists(tempPdfPath))
                    {
                        // Retry deletion up to 3 times (handles file system delays)
                        for (int retry = 0; retry < 3; retry++)
                        {
                            try
                            {
                                File.Delete(tempPdfPath);
                                _logger.LogDebug("Deleted temporary PDF file: {Path}", tempPdfPath);
                                break;
                            }
                            catch (IOException) when (retry < 2)
                            {
                                // File might still be locked, wait and retry
                                await Task.Delay(200);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete temporary PDF file: {Path}", tempPdfPath);
                    // Don't throw - temp files will be cleaned up eventually
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering translated images from PDF");
            return new List<ExtractedImage>();
        }
    }

    private byte[] ConvertBgraToPng(byte[] bgraData, int width, int height)
    {
        // Convert BGRA (4 bytes per pixel) to PNG format using SixLabors.ImageSharp
        // ImageSharp is pure .NET with no native dependencies, works in Docker Linux
        try
        {
            // Create image from BGRA data
            // SixLabors.ImageSharp uses Bgra32 pixel format (4 bytes: B, G, R, A)
            using var image = Image.LoadPixelData<Bgra32>(bgraData, width, height);
            
            // Encode to PNG
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting BGRA to PNG using ImageSharp");
            throw;
        }
    }

    private byte[] ConvertAndResizeBgraToPng(byte[] bgraData, int sourceWidth, int sourceHeight, 
        int targetWidth, int targetHeight)
    {
        // Convert BGRA to image and resize to match original dimensions
        try
        {
            // Create image from BGRA data
            using var image = Image.LoadPixelData<Bgra32>(bgraData, sourceWidth, sourceHeight);
            
            // Only resize if dimensions don't match
            if (sourceWidth != targetWidth || sourceHeight != targetHeight)
            {
                _logger.LogDebug("Resizing image from {SourceW}x{SourceH} to {TargetW}x{TargetH}",
                    sourceWidth, sourceHeight, targetWidth, targetHeight);
                    
                // Resize using high-quality Lanczos3 resampler
                image.Mutate(x => x.Resize(targetWidth, targetHeight, KnownResamplers.Lanczos3));
            }
            
            // Encode to PNG
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting and resizing BGRA to PNG. Source: {SW}x{SH}, Target: {TW}x{TH}",
                sourceWidth, sourceHeight, targetWidth, targetHeight);
            throw;
        }
    }
}
