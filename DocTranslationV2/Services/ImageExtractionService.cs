using DocTranslationV2.Models;
using DocTranslationV2.Constants;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using PdfDocument = iText.Kernel.Pdf.PdfDocument;
using PdfReader = iText.Kernel.Pdf.PdfReader;
using PdfWriter = iText.Kernel.Pdf.PdfWriter;
using ITextDocument = iText.Layout.Document;
using ITextImage = iText.Layout.Element.Image;
using ITextParagraph = iText.Layout.Element.Paragraph;
using iText.Layout.Element;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using P = DocumentFormat.OpenXml.Presentation;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using ImageMagick;
using Azure.Storage.Blobs;

namespace DocTranslationV2.Services;

public class ImageExtractionService : IImageExtractionService
{
    private readonly ILogger<ImageExtractionService> _logger;
    private readonly IPythonPdfService? _pythonPdfService;
    private readonly bool _usePythonForPdf;
    private readonly ImageFilteringSettings _filterSettings;
    private readonly DiagnosticSettings _diagnosticSettings;
    private readonly AzureBlobStorageSettings _blobSettings;
    private readonly IConfiguration _configuration;
    private readonly ICredentialService _credentialService;

    public ImageExtractionService(
        ILogger<ImageExtractionService> logger,
        IConfiguration configuration,
        IOptions<TranslationConfiguration> config,
        ICredentialService credentialService,
        IPythonPdfService? pythonPdfService = null)
    {
        _logger = logger;
        _configuration = configuration;
        _credentialService = credentialService;
        _pythonPdfService = pythonPdfService;
        _usePythonForPdf = configuration.GetValue<bool>("PythonPdfService:Enabled", false);
        _filterSettings = config.Value.ImageFiltering;
        _diagnosticSettings = config.Value.Diagnostics;
        _blobSettings = config.Value.AzureBlobStorage;
        
        _logger.LogInformation("Image filtering settings - TextFilter: {TextFilter}, DecorativeFilter: {DecorativeFilter}, MinSize: {MinSize} bytes", 
            _filterSettings.FilterImagesWithContainedText, 
            _filterSettings.FilterDecorativeImages,
            _filterSettings.MinimumImageSizeBytes);
        
        _logger.LogInformation("Diagnostic settings - ImageUpload: {ImageUpload}, PdfDimensionValidation: {PdfValidation}", 
            _diagnosticSettings.EnableImageUpload,
            _diagnosticSettings.EnablePdfDimensionValidation);
    }

    public async Task<DocumentImageInfo> ExtractImagesFromPdfAsync(Stream pdfStream, string fileName, ImageFilteringOptions? filteringOptions = null)
    {
        // Use provided options or fall back to config defaults
        var filterSettings = filteringOptions ?? new ImageFilteringOptions
        {
            FilterImagesWithContainedText = _filterSettings.FilterImagesWithContainedText,
            FilterDecorativeImages = _filterSettings.FilterDecorativeImages,
            MinimumImageSizeBytes = _filterSettings.MinimumImageSizeBytes,
            MinimumImageWidthPixels = _filterSettings.MinimumImageWidthPixels,
            MinimumImageHeightPixels = _filterSettings.MinimumImageHeightPixels
        };
        
        _logger.LogInformation("Extracting images from PDF: {FileName} with filtering - TextFilter: {TextFilter}, DecorativeFilter: {DecorativeFilter}", 
            fileName, filterSettings.FilterImagesWithContainedText, filterSettings.FilterDecorativeImages);
        
        var documentInfo = new DocumentImageInfo
        {
            OriginalFilePath = fileName,
            Images = new List<ExtractedImage>(),
            DocumentType = "pdf"
        };

        try
        {

            using var pdfDocument = new PdfDocument(new PdfReader(pdfStream));
            var imageIndex = 0;
            var hasTextContent = false;

            for (int pageNum = 1; pageNum <= pdfDocument.GetNumberOfPages(); pageNum++)
            {
                var page = pdfDocument.GetPage(pageNum);
                
                // Check if page has text content
                if (!hasTextContent)
                {
                    var text = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        hasTextContent = true;
                        _logger.LogInformation("PDF {FileName} contains text content on page {PageNum}", fileName, pageNum);
                    }
                }

                var resources = page.GetResources();
                if (resources == null)
                {
                    _logger.LogInformation("No resources found on page {PageNum}", pageNum);
                    continue;
                }

                var xObjects = resources.GetResourceNames();
                if (xObjects == null || !xObjects.Any())
                {
                    _logger.LogInformation("No XObjects found on page {PageNum}", pageNum);
                    continue;
                }

                _logger.LogInformation("Found {Count} XObjects on page {PageNum}", xObjects.Count(), pageNum);

                foreach (var xObjectName in xObjects)
                {
                    try
                    {
                        // Get the XObject using GetResourceObject which returns a PdfObject
                        var xObject = resources.GetResourceObject(iText.Kernel.Pdf.PdfName.XObject, xObjectName);
                        
                        if (xObject == null)
                        {
                            _logger.LogInformation("XObject {Name} is null on page {PageNum}", xObjectName, pageNum);
                            continue;
                        }

                        _logger.LogInformation("XObject {Name} on page {PageNum} found, type: {Type}", 
                            xObjectName, pageNum, xObject.GetType().Name);

                        // XObjects are stored as indirect references, so we need to get the actual object
                        var pdfObject = xObject.IsIndirectReference() 
                            ? ((iText.Kernel.Pdf.PdfIndirectReference)xObject).GetRefersTo() 
                            : xObject;

                        if (pdfObject == null)
                        {
                            _logger.LogInformation("XObject {Name} indirect reference is null on page {PageNum}", xObjectName, pageNum);
                            continue;
                        }

                        // Check if it's a stream (images are stored as streams)
                        if (pdfObject is PdfStream stream)
                        {
                            var subType = stream.GetAsName(iText.Kernel.Pdf.PdfName.Subtype);
                            
                            _logger.LogInformation("XObject {Name} on page {PageNum} has subtype: {SubType}", 
                                xObjectName, pageNum, subType?.GetValue());
                            
                            if (subType != null && subType.Equals(iText.Kernel.Pdf.PdfName.Image))
                            {
                                try
                                {
                                    var pdfImageXObject = new PdfImageXObject(stream);
                                    var imageData = pdfImageXObject.GetImageBytes();
                                    var imageWidth = (int)pdfImageXObject.GetWidth();
                                    var imageHeight = (int)pdfImageXObject.GetHeight();
                                    
                                    // Apply size filter if enabled
                                    if (imageData.Length < filterSettings.MinimumImageSizeBytes)
                                    {
                                        _logger.LogInformation("Skipping tiny image on page {PageNum} (size: {Size} bytes, threshold: {Threshold})", 
                                            pageNum, imageData.Length, filterSettings.MinimumImageSizeBytes);
                                        continue;
                                    }
                                    
                                    // Apply dimension filter if enabled
                                    if (imageWidth < filterSettings.MinimumImageWidthPixels || 
                                        imageHeight < filterSettings.MinimumImageHeightPixels)
                                    {
                                        _logger.LogInformation("Skipping small image on page {PageNum} (dimensions: {Width}x{Height}, threshold: {MinW}x{MinH})", 
                                            pageNum, imageWidth, imageHeight, 
                                            filterSettings.MinimumImageWidthPixels, filterSettings.MinimumImageHeightPixels);
                                        continue;
                                    }
                                    
                                    // Check if text is positioned within the image boundary (if filtering enabled)
                                    // This indicates the image is just a background for text (styled title, etc.)
                                    if (filterSettings.FilterImagesWithContainedText && 
                                        HasTextWithinImageBoundary(page, xObjectName.GetValue()))
                                    {
                                        // Note: Detailed logging happens in HasTextWithinImageBoundary method
                                        continue;
                                    }
                                    
                                    // Skip decorative images (backgrounds, shading, borders, etc.) if filtering enabled
                                    if (filterSettings.FilterDecorativeImages && 
                                        IsLikelyDecorativeImage(imageData, imageWidth, imageHeight))
                                    {
                                        _logger.LogInformation("Skipping decorative image on page {PageNum}: {Width}x{Height}, {Size} bytes",
                                            pageNum, imageWidth, imageHeight, imageData.Length);
                                        continue;
                                    }
                                    
                                    var imageId = $"pdf_page{pageNum}_img{imageIndex}";
                        
                                    _logger.LogInformation("Extracted image {ImageId} from page {PageNum} (size: {Size} bytes, dimensions: {Width}x{Height})", 
                                        imageId, pageNum, imageData.Length, imageWidth, imageHeight);
                                    
                                    documentInfo.Images.Add(new ExtractedImage
                                    {
                                        PageNumber = pageNum,
                                        ImageIndex = imageIndex,
                                        ImageName = $"image_{imageIndex}.png",
                                        ImageData = imageData,
                                        Format = "png",
                                        ImageId = imageId,
                                        RelationshipId = xObjectName.ToString(),
                                        OriginalSize = imageData.Length,
                                        Width = imageWidth,
                                        Height = imageHeight,
                                        Position = new ImagePosition
                                        {
                                            X = 0, // PDF position extraction requires more complex logic
                                            Y = 0,
                                            Width = imageWidth,
                                            Height = imageHeight,
                                            PositionType = "embedded"
                                        }
                                    });
                                    
                                    imageIndex++;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Could not extract image {Name} from page {PageNum}", xObjectName, pageNum);
                                }
                            }
                            else if (subType != null && subType.Equals(iText.Kernel.Pdf.PdfName.Form))
                            {
                                // Form XObjects might contain images - recursively check
                                _logger.LogInformation("Found Form XObject on page {PageNum}, checking for embedded images", pageNum);
                                // Note: Recursive extraction of images from Form XObjects can be added here if needed
                            }
                        }
                        else
                        {
                            _logger.LogInformation("XObject {Name} on page {PageNum} is not a PdfStream (type: {Type})", 
                                xObjectName, pageNum, pdfObject.GetType().Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error processing XObject {Name} on page {PageNum}", xObjectName, pageNum);
                    }
                }
            }

            documentInfo.HasImages = documentInfo.Images.Any();
            documentInfo.HasTextContent = hasTextContent;

            if (documentInfo.HasImages && !hasTextContent)
            {
                _logger.LogWarning("PDF {FileName} contains only images with no text content. " +
                    "Azure Translation Service can translate this directly without image preprocessing.", fileName);
            }

            _logger.LogInformation("Extracted {ImageCount} images from PDF. HasText: {HasText}", 
                documentInfo.Images.Count, hasTextContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting images from PDF: {FileName}", fileName);
            throw;
        }

        return documentInfo;
    }

    private bool HasTextWithinImageBoundary(PdfPage page, string xObjectName)
    {
        // Check if there's text positioned within the image's bounding box
        // This indicates the image is just a background for text (styled title, etc.)
        try
        {
            // Create a custom listener to track both image and text positions
            var listener = new TextAndImagePositionListener(xObjectName);
            
            // Process the page to extract positions
            var processor = new iText.Kernel.Pdf.Canvas.Parser.PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);
            
            // Check if we found overlap
            var (hasText, containedText, imageRect, nearbyText, overlappingText) = listener.GetTextAnalysis();
            if (hasText)
            {
                _logger.LogWarning("SKIPPING image {Name} - Text found WITHIN image boundary (styled title/background)", 
                    xObjectName);
                _logger.LogInformation("  Image bounds: X={X:F1}, Y={Y:F1}, W={W:F1}, H={H:F1}", 
                    imageRect?.GetLeft(), imageRect?.GetBottom(), imageRect?.GetWidth(), imageRect?.GetHeight());
                _logger.LogInformation("  Text CONTAINED in image ({Count} chunks): {Text}", 
                    containedText.Count, string.Join(" | ", containedText.Select(t => $"'{t}'")));
                
                if (overlappingText.Any())
                {
                    _logger.LogInformation("  Text OVERLAPPING (but not contained): {Text}", 
                        string.Join(" | ", overlappingText.Select(t => $"'{t}'")));
                }
                
                return true;
            }
            
            // Log if text is nearby but NOT contained (this is OK - we still extract the image)
            if (overlappingText.Any() || nearbyText.Any())
            {
                _logger.LogInformation("Image {Name} has nearby/overlapping text but NOT contained - OK to extract", xObjectName);
                if (overlappingText.Any())
                {
                    _logger.LogInformation("  Text overlapping image edge: {Text}", 
                        string.Join(" | ", overlappingText.Take(3).Select(t => $"'{t}'")));
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for text within image boundary, assuming no overlap");
            return false; // If we can't determine, assume no text overlap
        }
    }
    
    // Helper class to track both text and image positions during PDF processing
    private class TextAndImagePositionListener : iText.Kernel.Pdf.Canvas.Parser.Listener.IEventListener
    {
        private readonly string _targetImageName;
        private iText.Kernel.Geom.Rectangle? _imageRect;
        private readonly List<(iText.Kernel.Geom.Rectangle rect, string text)> _textRects = new();
        
        public TextAndImagePositionListener(string targetImageName)
        {
            _targetImageName = targetImageName;
        }
        
        public void EventOccurred(iText.Kernel.Pdf.Canvas.Parser.Data.IEventData data, iText.Kernel.Pdf.Canvas.Parser.EventType type)
        {
            if (type == iText.Kernel.Pdf.Canvas.Parser.EventType.RENDER_IMAGE)
            {
                var renderInfo = (iText.Kernel.Pdf.Canvas.Parser.Data.ImageRenderInfo)data;
                var imageName = renderInfo.GetImageResourceName()?.GetValue();
                
                if (imageName == _targetImageName)
                {
                    // Get the image's transformation matrix to calculate its position
                    var ctm = renderInfo.GetImageCtm();
                    
                    // Calculate the image rectangle from the transformation matrix
                    var x = ctm.Get(iText.Kernel.Geom.Matrix.I31);
                    var y = ctm.Get(iText.Kernel.Geom.Matrix.I32);
                    var width = Math.Abs(ctm.Get(iText.Kernel.Geom.Matrix.I11));
                    var height = Math.Abs(ctm.Get(iText.Kernel.Geom.Matrix.I22));
                    
                    _imageRect = new iText.Kernel.Geom.Rectangle((float)x, (float)y, (float)width, (float)height);
                }
            }
            else if (type == iText.Kernel.Pdf.Canvas.Parser.EventType.RENDER_TEXT)
            {
                var renderInfo = (iText.Kernel.Pdf.Canvas.Parser.Data.TextRenderInfo)data;
                
                // Check if text actually has content (not just whitespace or invisible text)
                var text = renderInfo.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Skip empty text, whitespace-only, or invisible text
                    return;
                }
                
                var baseline = renderInfo.GetBaseline();
                var ascentLine = renderInfo.GetAscentLine();
                
                // Create rectangle for the text
                var x = Math.Min(baseline.GetStartPoint().Get(0), ascentLine.GetStartPoint().Get(0));
                var y = Math.Min(baseline.GetStartPoint().Get(1), ascentLine.GetStartPoint().Get(1));
                var width = Math.Abs(baseline.GetEndPoint().Get(0) - baseline.GetStartPoint().Get(0));
                var height = Math.Abs(ascentLine.GetStartPoint().Get(1) - baseline.GetStartPoint().Get(1));
                
                var textRect = new iText.Kernel.Geom.Rectangle(x, y, width, height);
                _textRects.Add((textRect, text));
            }
        }
        
        public ICollection<iText.Kernel.Pdf.Canvas.Parser.EventType> GetSupportedEvents()
        {
            return new HashSet<iText.Kernel.Pdf.Canvas.Parser.EventType> 
            { 
                iText.Kernel.Pdf.Canvas.Parser.EventType.RENDER_IMAGE,
                iText.Kernel.Pdf.Canvas.Parser.EventType.RENDER_TEXT
            };
        }
        
        public bool HasTextWithinImageBoundary()
        {
            var (hasText, _, _, _, _) = GetTextAnalysis();
            return hasText;
        }
        
        public (bool hasText, List<string> containedText, iText.Kernel.Geom.Rectangle? imageRect, List<string> nearbyText, List<string> overlappingText) GetTextAnalysis()
        {
            var containedText = new List<string>();
            var overlappingText = new List<string>();
            var nearbyText = new List<string>();
            
            if (_imageRect == null || !_textRects.Any())
                return (false, containedText, _imageRect, nearbyText, overlappingText);
            
            // Analyze each text chunk's relationship to the image
            foreach (var (textRect, text) in _textRects)
            {
                if (IsTextContainedInImage(_imageRect, textRect))
                {
                    // Text is fully inside image
                    containedText.Add(text);
                }
                else if (RectanglesOverlap(_imageRect, textRect))
                {
                    // Text overlaps image edge but isn't fully contained
                    overlappingText.Add(text);
                }
                else if (IsNearby(_imageRect, textRect, 50)) // Within 50 units
                {
                    // Text is close to image but not touching
                    nearbyText.Add(text);
                }
            }
            
            return (containedText.Any(), containedText, _imageRect, nearbyText, overlappingText);
        }
        
        private bool RectanglesOverlap(iText.Kernel.Geom.Rectangle rect1, iText.Kernel.Geom.Rectangle rect2)
        {
            return !(rect1.GetRight() < rect2.GetLeft() ||
                    rect1.GetLeft() > rect2.GetRight() ||
                    rect1.GetTop() < rect2.GetBottom() ||
                    rect1.GetBottom() > rect2.GetTop());
        }
        
        private bool IsNearby(iText.Kernel.Geom.Rectangle rect1, iText.Kernel.Geom.Rectangle rect2, float distance)
        {
            // Check if rectangles are within distance of each other
            var horizontalGap = Math.Max(0, Math.Max(rect1.GetLeft() - rect2.GetRight(), rect2.GetLeft() - rect1.GetRight()));
            var verticalGap = Math.Max(0, Math.Max(rect1.GetBottom() - rect2.GetTop(), rect2.GetBottom() - rect1.GetTop()));
            
            return horizontalGap <= distance && verticalGap <= distance;
        }
        
        private bool IsTextContainedInImage(iText.Kernel.Geom.Rectangle imageRect, iText.Kernel.Geom.Rectangle textRect)
        {
            // Check if text rectangle is fully contained within the image rectangle
            // Text must be completely inside the image boundaries
            return textRect.GetLeft() >= imageRect.GetLeft() &&
                   textRect.GetRight() <= imageRect.GetRight() &&
                   textRect.GetBottom() >= imageRect.GetBottom() &&
                   textRect.GetTop() <= imageRect.GetTop();
        }
    }

    private bool IsLikelyDecorativeImage(byte[] imageData, int width, int height)
    {
        // Filter out decorative images (backgrounds, shading, borders, etc.)
        try
        {
            // 1. Skip very small images (< 100 bytes already filtered above)
            
            // 2. Skip images with unusual aspect ratios (likely decorative borders/lines)
            var aspectRatio = (double)width / height;
            if (aspectRatio > 20 || aspectRatio < 0.05) // Very wide or very tall
            {
                _logger.LogDebug("Skipping decorative image with extreme aspect ratio: {Width}x{Height} (ratio: {Ratio:F2})",
                    width, height, aspectRatio);
                return true;
            }
            
            // 3. Skip very small dimensions (likely icons or decorative elements under 32px)
            if (width < 32 && height < 32)
            {
                _logger.LogDebug("Skipping tiny decorative image: {Width}x{Height}", width, height);
                return true;
            }
            
            // 4. Check if image is solid color or near-solid (background shading)
            // Sample the image data to check color variance
            if (imageData.Length > 100)
            {
                // Sample first 100 bytes to check for patterns
                // If all bytes are very similar, it's likely a solid color background
                var sampleSize = Math.Min(100, imageData.Length);
                var firstByte = imageData[0];
                var similarBytes = 0;
                
                for (int i = 0; i < sampleSize; i += 4) // Sample every 4th byte (RGBA)
                {
                    if (Math.Abs(imageData[i] - firstByte) < 10)
                    {
                        similarBytes++;
                    }
                }
                
                var similarityRatio = (double)similarBytes / (sampleSize / 4);
                if (similarityRatio > 0.95) // 95% of sampled bytes are very similar
                {
                    _logger.LogDebug("Skipping solid-color/background image: {Width}x{Height} (similarity: {Ratio:F2})",
                        width, height, similarityRatio);
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if image is decorative, assuming it's content");
            return false; // If we can't determine, assume it's content
        }
    }

    public async Task<DocumentImageInfo> ExtractImagesFromWordAsync(Stream wordStream, string fileName, ImageFilteringOptions? filteringOptions = null)
    {
        // Use provided options or fall back to config defaults
        var filterSettings = filteringOptions ?? new ImageFilteringOptions
        {
            FilterImagesWithContainedText = _filterSettings.FilterImagesWithContainedText,
            FilterDecorativeImages = _filterSettings.FilterDecorativeImages,
            MinimumImageSizeBytes = _filterSettings.MinimumImageSizeBytes,
            MinimumImageWidthPixels = _filterSettings.MinimumImageWidthPixels,
            MinimumImageHeightPixels = _filterSettings.MinimumImageHeightPixels
        };
        
        _logger.LogInformation("Extracting images from Word document: {FileName} with filtering - TextFilter: {TextFilter}, DecorativeFilter: {DecorativeFilter}", 
            fileName, filterSettings.FilterImagesWithContainedText, filterSettings.FilterDecorativeImages);
        
        var documentInfo = new DocumentImageInfo
        {
            OriginalFilePath = fileName,
            Images = new List<ExtractedImage>(),
            DocumentType = "docx"
        };

        try
        {

            var memoryStream = new MemoryStream();
            await wordStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var wordDocument = WordprocessingDocument.Open(memoryStream, false);
            var mainPart = wordDocument.MainDocumentPart;
            
            if (mainPart == null)
            {
                return documentInfo;
            }

            // Check for text content
            var body = mainPart.Document.Body;
            var hasTextContent = body?.InnerText != null && !string.IsNullOrWhiteSpace(body.InnerText);
            documentInfo.HasTextContent = hasTextContent;

            if (hasTextContent)
            {
                _logger.LogInformation("Word document {FileName} contains text content", fileName);
            }
            else
            {
                _logger.LogWarning("Word document {FileName} contains no text content", fileName);
            }

            var imageIndex = 0;
            
            // Create a mapping of image parts to their relationship IDs
            var imagePartRelationships = new Dictionary<ImagePart, string>();
            foreach (var rel in mainPart.ImageParts)
            {
                var relationshipId = mainPart.GetIdOfPart(rel);
                imagePartRelationships[rel] = relationshipId;
            }

            // Extract images with their relationship IDs for proper tracking
            foreach (var kvp in imagePartRelationships)
            {
                var imagePart = kvp.Key;
                var relationshipId = kvp.Value;
                
                using var imageStream = imagePart.GetStream();
                using var ms = new MemoryStream();
                await imageStream.CopyToAsync(ms);
                
                var imageData = ms.ToArray();
                var contentType = imagePart.ContentType;
                var format = GetImageFormat(contentType);
                var imageId = $"word_img{imageIndex}_{relationshipId}";

                // Try to get image dimensions using ImageSharp (cross-platform)
                int width = 0, height = 0;
                try
                {
                    using var imgStream = new MemoryStream(imageData);
                    using var img = SixLabors.ImageSharp.Image.Load(imgStream);
                    width = img.Width;
                    height = img.Height;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not determine dimensions for image {ImageId}", imageId);
                }
                
                // Apply size filter if enabled
                if (imageData.Length < filterSettings.MinimumImageSizeBytes)
                {
                    _logger.LogInformation("Skipping tiny image in Word doc (size: {Size} bytes, threshold: {Threshold})", 
                        imageData.Length, filterSettings.MinimumImageSizeBytes);
                    continue;
                }
                
                // Apply dimension filter if enabled
                if (width > 0 && height > 0 && 
                    (width < filterSettings.MinimumImageWidthPixels || height < filterSettings.MinimumImageHeightPixels))
                {
                    _logger.LogInformation("Skipping small image in Word doc (dimensions: {Width}x{Height}, threshold: {MinW}x{MinH})", 
                        width, height, filterSettings.MinimumImageWidthPixels, filterSettings.MinimumImageHeightPixels);
                    continue;
                }
                
                // Skip decorative images if filtering enabled
                if (filterSettings.FilterDecorativeImages && IsLikelyDecorativeImage(imageData, width, height))
                {
                    _logger.LogInformation("Skipping decorative image in Word doc: {Width}x{Height}, {Size} bytes",
                        width, height, imageData.Length);
                    continue;
                }

                documentInfo.Images.Add(new ExtractedImage
                {
                    PageNumber = 0, // Word doesn't have traditional pages in XML
                    ImageIndex = imageIndex,
                    ImageName = $"image_{imageIndex}.{format}",
                    ImageData = imageData,
                    Format = format,
                    ImageId = imageId,
                    RelationshipId = relationshipId,
                    OriginalSize = imageData.Length,
                    Width = width,
                    Height = height,
                    Position = new ImagePosition
                    {
                        PositionType = "inline" // Will be determined by document structure
                    }
                });
                
                imageIndex++;
            }

            documentInfo.HasImages = documentInfo.Images.Any();

            if (documentInfo.HasImages && !hasTextContent)
            {
                _logger.LogWarning("Word document {FileName} contains only images with no text content. " +
                    "Consider if image preprocessing is necessary.", fileName);
            }

            _logger.LogInformation("Extracted {ImageCount} images from Word document. HasText: {HasText}", 
                documentInfo.Images.Count, hasTextContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting images from Word document: {FileName}", fileName);
            throw;
        }

        return documentInfo;
    }

    public async Task<DocumentImageInfo> ExtractImagesFromPowerPointAsync(Stream pptxStream, string fileName, ImageFilteringOptions? filteringOptions = null)
    {
        // Use provided options or fall back to config defaults
        var filterSettings = filteringOptions ?? new ImageFilteringOptions
        {
            FilterImagesWithContainedText = _filterSettings.FilterImagesWithContainedText,
            FilterDecorativeImages = _filterSettings.FilterDecorativeImages,
            MinimumImageSizeBytes = _filterSettings.MinimumImageSizeBytes,
            MinimumImageWidthPixels = _filterSettings.MinimumImageWidthPixels,
            MinimumImageHeightPixels = _filterSettings.MinimumImageHeightPixels
        };
        
        _logger.LogInformation("Extracting images from PowerPoint: {FileName} with filtering - TextFilter: {TextFilter}, DecorativeFilter: {DecorativeFilter}", 
            fileName, filterSettings.FilterImagesWithContainedText, filterSettings.FilterDecorativeImages);
        
        var documentInfo = new DocumentImageInfo
        {
            OriginalFilePath = fileName,
            Images = new List<ExtractedImage>(),
            DocumentType = "pptx"
        };

        try
        {
            var memoryStream = new MemoryStream();
            await pptxStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            using var presentationDocument = PresentationDocument.Open(memoryStream, false);
            var presentationPart = presentationDocument.PresentationPart;
            
            if (presentationPart == null)
            {
                _logger.LogWarning("No presentation part found in {FileName}", fileName);
                return documentInfo;
            }

            // Check for text content across all slides
            var hasTextContent = false;
            var slideIndex = 0;
            var imageIndex = 0;

            // Get all slides
            var slideIds = presentationPart.Presentation?.SlideIdList?.Elements<P.SlideId>();
            if (slideIds == null || !slideIds.Any())
            {
                _logger.LogWarning("No slides found in PowerPoint {FileName}", fileName);
                return documentInfo;
            }

            _logger.LogInformation("Found {SlideCount} slides in PowerPoint {FileName}", slideIds.Count(), fileName);

            foreach (var slideId in slideIds)
            {
                slideIndex++;
                
                // Null-check for PresentationPart (already checked above, but needed for compiler)
                if (presentationPart == null)
                {
                    _logger.LogWarning("PresentationPart became null unexpectedly at slide {SlideIndex}", slideIndex);
                    continue;
                }
                
                var slidePart = presentationPart.GetPartById(slideId.RelationshipId!) as SlidePart;
                if (slidePart == null)
                {
                    _logger.LogWarning("Could not load slide {SlideIndex}", slideIndex);
                    continue;
                }

                // Check for text content on this slide
                if (!hasTextContent && slidePart.Slide?.InnerText != null && !string.IsNullOrWhiteSpace(slidePart.Slide.InnerText))
                {
                    hasTextContent = true;
                    _logger.LogInformation("PowerPoint {FileName} contains text content on slide {SlideIndex}", fileName, slideIndex);
                }

                // Create a mapping of image parts to their relationship IDs for this slide
                var imagePartRelationships = new Dictionary<ImagePart, string>();
                foreach (var imagePart in slidePart.ImageParts)
                {
                    var relationshipId = slidePart.GetIdOfPart(imagePart);
                    imagePartRelationships[imagePart] = relationshipId;
                }

                _logger.LogInformation("Slide {SlideIndex}: Found {ImageCount} image parts", slideIndex, imagePartRelationships.Count);

                // Build a map of relationship IDs to their z-order position in the slide
                // Z-order is determined by the position in the shape tree: earlier = behind, later = in front
                // IMPORTANT: This must include BOTH Picture elements AND GraphicFrame elements (Visio diagrams, embedded objects)
                var relationshipZOrder = new Dictionary<string, int>();
                if (slidePart.Slide != null)
                {
                    var shapeTree = slidePart.Slide.CommonSlideData?.ShapeTree;
                    if (shapeTree != null)
                    {
                        int zOrderIndex = 0;
                        foreach (var element in shapeTree.ChildElements)
                        {
                            // Check if this element is a Picture
                            if (element is P.Picture picture)
                            {
                                var blip = picture.Descendants<A.Blip>().FirstOrDefault();
                                if (blip?.Embed?.Value != null)
                                {
                                    relationshipZOrder[blip.Embed.Value] = zOrderIndex;
                                    _logger.LogInformation("  ? Picture with relationship {RelId} assigned z-order {ZOrder}", 
                                        blip.Embed.Value, zOrderIndex);
                                }
                            }
                            // ALSO check if this element is a GraphicFrame (Visio diagrams, embedded objects)
                            else if (element is P.GraphicFrame graphicFrame)
                            {
                                // GraphicFrames can contain embedded images via Blip references
                                var blip = graphicFrame.Descendants<A.Blip>().FirstOrDefault();
                                if (blip?.Embed?.Value != null)
                                {
                                    relationshipZOrder[blip.Embed.Value] = zOrderIndex;
                                    _logger.LogInformation("  ? GraphicFrame (Visio/Object) with relationship {RelId} assigned z-order {ZOrder}", 
                                        blip.Embed.Value, zOrderIndex);
                                }
                            }
                            zOrderIndex++;
                        }
                        
                        _logger.LogInformation("Slide {SlideIndex}: Captured z-order for {Count} pictures/objects", 
                            slideIndex, relationshipZOrder.Count);
                    }
                    else
                    {
                        _logger.LogWarning("Slide {SlideIndex}: No shape tree found - z-order cannot be captured", slideIndex);
                    }
                }

                // Extract images from this slide
                foreach (var kvp in imagePartRelationships)
                {
                    var imagePart = kvp.Key;
                    var relationshipId = kvp.Value;
                    
                    using var imageStream = imagePart.GetStream();
                    using var ms = new MemoryStream();
                    await imageStream.CopyToAsync(ms);
                    
                    var imageData = ms.ToArray();
                    var contentType = imagePart.ContentType;
                    var format = GetImageFormat(contentType);
                    var imageId = $"pptx_slide{slideIndex}_img{imageIndex}_{relationshipId}";

                    // Get z-order for this image (supports both Picture and GraphicFrame)
                    int? zOrder = relationshipZOrder.ContainsKey(relationshipId) 
                        ? relationshipZOrder[relationshipId] 
                        : (int?)null;
                    
                    if (zOrder.HasValue)
                    {
                        _logger.LogInformation("  ? Image {ImageId} has z-order {ZOrder} (relationship {RelId})", 
                            imageId, zOrder.Value, relationshipId);
                    }
                    else
                    {
                        _logger.LogWarning("  ? Image {ImageId} has NO z-order captured (relationship {RelId})", 
                            imageId, relationshipId);
                    }

                    // Try to get image dimensions and detect EMF/WMF
                    int width = 0, height = 0;
                    bool isConvertedFormat = false;
                    byte[] processedImageData = imageData; // Will be converted if EMF/WMF
                    
                    // Check if this is EMF/WMF format first (before trying to load with ImageSharp)
                    var contentTypeLower = contentType.ToLowerInvariant();
                    bool isMetafile = contentTypeLower.Contains("emf") || 
                                     contentTypeLower.Contains("wmf") ||
                                     contentTypeLower.Contains("x-emf") ||
                                     contentTypeLower.Contains("x-wmf") ||
                                     contentTypeLower.Contains("x-ms-wmf");
                    
                    if (isMetafile)
                    {
                        _logger.LogInformation("Detected metafile format {ContentType} for {ImageId}", 
                            contentType, imageId);
                        
                        // For EMF/WMF, we need to convert to PNG to get the native resolution
                        // The display dimensions from PowerPoint are NOT the native resolution
                        try
                        {
                            // Convert EMF/WMF to PNG - this will use the native resolution
                            processedImageData = ConvertEmfWmfToPng(imageData, 0, 0, contentType);
                            format = "png";
                            isConvertedFormat = true;
                            
                            // Now get the ACTUAL dimensions from the converted PNG
                            using var pngStream = new MemoryStream(processedImageData);
                            using var pngImage = SixLabors.ImageSharp.Image.Load(pngStream);
                            width = pngImage.Width;   // This is the NATIVE resolution
                            height = pngImage.Height; // This is the NATIVE resolution
                        }
                        catch (Exception convertEx)
                        {
                            _logger.LogError(convertEx, "Failed to convert {ContentType} to PNG for {ImageId}, will skip", 
                                contentType, imageId);
                            continue;
                        }
                    }
                    else
                    {
                        // For regular images (PNG, JPEG, etc.), use ImageSharp
                        try
                        {
                            using var imgStream = new MemoryStream(imageData);
                            using var img = SixLabors.ImageSharp.Image.Load(imgStream);
                            width = img.Width;
                            height = img.Height;
                            _logger.LogDebug("Detected standard image format {ContentType}: {Width}x{Height}", 
                                contentType, width, height);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not determine dimensions for image {ImageId}, will skip", imageId);
                            continue;
                        }
                    }
                
                // Apply size filter if enabled
                if (processedImageData.Length < filterSettings.MinimumImageSizeBytes)
                {
                    _logger.LogInformation("Skipping tiny image on slide {SlideIndex} (size: {Size} bytes, threshold: {Threshold})", 
                        slideIndex, processedImageData.Length, filterSettings.MinimumImageSizeBytes);
                    continue;
                }
                
                // Apply dimension filter if enabled
                if (width > 0 && height > 0 && 
                    (width < filterSettings.MinimumImageWidthPixels || height < filterSettings.MinimumImageHeightPixels))
                {
                    _logger.LogInformation("Skipping small image on slide {SlideIndex} (dimensions: {Width}x{Height}, threshold: {MinW}x{MinH})", 
                        slideIndex, width, height, filterSettings.MinimumImageWidthPixels, filterSettings.MinimumImageHeightPixels);
                    continue;
                }
                
                // Skip decorative images if filtering enabled
                if (filterSettings.FilterDecorativeImages && IsLikelyDecorativeImage(processedImageData, width, height))
                {
                    _logger.LogInformation("Skipping decorative image on slide {SlideIndex}: {Width}x{Height}, {Size} bytes",
                        slideIndex, width, height, processedImageData.Length);
                    continue;
                }

                documentInfo.Images.Add(new ExtractedImage
                {
                    PageNumber = slideIndex, // Use slide number as page number
                    ImageIndex = imageIndex,
                    ImageName = $"slide{slideIndex}_image_{imageIndex}.{format}",
                    ImageData = processedImageData, // Use converted PNG data for EMF/WMF
                    Format = format,
                    ImageId = imageId,
                    RelationshipId = relationshipId,
                    OriginalSize = processedImageData.Length,
                    Width = width,
                    Height = height,
                    Position = new ImagePosition
                    {
                        PositionType = "slide", // PowerPoint-specific position type
                        ZOrder = zOrder // Capture z-order for layering (supports Picture and GraphicFrame)
                    }
                });
                
                imageIndex++;
                
                var conversionNote = isConvertedFormat ? " [Converted EMF/WMF?PNG]" : "";
                var zOrderNote = zOrder.HasValue ? $" Z-Order: {zOrder.Value}" : "";
                _logger.LogInformation("Extracted image {ImageId} from slide {SlideIndex} (size: {Size} bytes, dimensions: {Width}x{Height}{ConversionNote}{ZOrderNote})", 
                    imageId, slideIndex, processedImageData.Length, width, height, conversionNote, zOrderNote);
                }
            }

            documentInfo.HasImages = documentInfo.Images.Any();
            documentInfo.HasTextContent = hasTextContent;

            if (documentInfo.HasImages && !hasTextContent)
            {
                _logger.LogWarning("PowerPoint {FileName} contains only images with no text content. " +
                    "Consider if image preprocessing is necessary.", fileName);
            }

            _logger.LogInformation("Extracted {ImageCount} images from PowerPoint across {SlideCount} slides. HasText: {HasText}", 
                documentInfo.Images.Count, slideIndex, hasTextContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting images from PowerPoint: {FileName}", fileName);
            throw;
        }

        return documentInfo;
    }

    public async Task<Stream> CreatePdfFromImagesAsync(List<ExtractedImage> images, string jobId)
    {
        try
        {
            _logger.LogInformation("Creating PDF from {ImageCount} images for job {JobId}", images.Count, jobId);

            // DIAGNOSTIC: Upload images to blob storage for inspection (only if enabled in config)
            if (_diagnosticSettings.EnableImageUpload)
            {
                var diagnosticContainerName = ContainerNamePatterns.GetDiagnosticContainerName(jobId);
                _logger.LogInformation("?? DIAGNOSTIC: Starting upload of extracted images to blob storage container '{Container}'", 
                    diagnosticContainerName);
                
                try
                {
                    // Use the same authentication as the rest of the application (Client ID/Secret)
                    var blobUri = new Uri($"https://{_blobSettings.AccountName}.blob.core.windows.net");
                    var blobServiceClient = new BlobServiceClient(blobUri, _credentialService.GetBlobStorageCredential());
                    var containerClient = blobServiceClient.GetBlobContainerClient(diagnosticContainerName);
                    
                    _logger.LogInformation("Creating diagnostic container with Azure AD authentication...");
                    await containerClient.CreateIfNotExistsAsync();
                    
                    _logger.LogInformation("? Diagnostic container created: {Container}", diagnosticContainerName);
                    
                    foreach (var image in images.OrderBy(i => i.ImageIndex))
                    {
                        try
                        {
                            // Verify actual PNG dimensions before uploading
                            using var imgStream = new MemoryStream(image.ImageData);
                            using var img = SixLabors.ImageSharp.Image.Load(imgStream);
                            
                            var blobName = $"image_{image.ImageIndex}_metadata-{image.Width}x{image.Height}_actual-{img.Width}x{img.Height}.png";
                            var blobClient = containerClient.GetBlobClient(blobName);
                            
                            _logger.LogInformation("Uploading blob: {BlobName}", blobName);
                            await blobClient.UploadAsync(new MemoryStream(image.ImageData), overwrite: true);
                            
                            _logger.LogInformation("  ? Uploaded image {Index}: {BlobName}", image.ImageIndex, blobName);
                            _logger.LogInformation("     Metadata says: {MetaW}x{MetaH}, PNG is actually: {ActualW}x{ActualH}",
                                image.Width, image.Height, img.Width, img.Height);
                            
                            if (img.Width != image.Width || img.Height != image.Height)
                            {
                                _logger.LogError("     ? DIMENSION MISMATCH! PNG dimensions don't match metadata!");
                                _logger.LogError("     This means EMF/WMF conversion is creating wrong-sized PNGs!");
                            }
                            else
                            {
                                _logger.LogInformation("     ? Dimensions match - PNG is correct size");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Could not upload diagnostic image {Index}", image.ImageIndex);
                        }
                    }
                    
                    _logger.LogInformation("?? DIAGNOSTIC: All images uploaded to Azure Storage container '{Container}'", 
                        diagnosticContainerName);
                    _logger.LogInformation("   ?? Access via Azure Portal ? Storage Account ? Containers ? {Container}", 
                        diagnosticContainerName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "? Could not create diagnostic blob container - continuing anyway");
                }
            }
            else
            {
                _logger.LogDebug("Diagnostic image upload is disabled in configuration");
            }

            var outputStream = new MemoryStream();
            using var pdfWriter = new PdfWriter(outputStream);
            pdfWriter.SetCloseStream(false);
            
            using var pdfDocument = new PdfDocument(pdfWriter);

            foreach (var image in images.OrderBy(i => i.ImageIndex))
            {
                try
                {
                    // Load image data and set explicit DPI to control rendering
                    var imageData = iText.IO.Image.ImageDataFactory.Create(image.ImageData);
                    
                    // Get image dimensions in pixels
                    var imageWidthPixels = imageData.GetWidth();
                    var imageHeightPixels = imageData.GetHeight();
                    
                    // Set explicit DPI on the image data so Azure Translation Service renders at correct resolution
                    // Default is 72 DPI, but we want 96 DPI (Windows standard) to prevent scaling
                    imageData.SetDpi(96, 96);
                    
                    // With 96 DPI set on image, PDF points calculation:
                    // points = pixels * (72 PDF_DPI / 96 IMAGE_DPI) = pixels * 0.75
                    var imageWidthPoints = imageWidthPixels * 0.75f;
                    var imageHeightPoints = imageHeightPixels * 0.75f;
                    
                    _logger.LogDebug("Adding image {Index} with dimensions {WidthPx}x{HeightPx} pixels at 96 DPI ({WidthPt:F1}x{HeightPt:F1} points) to PDF", 
                        image.ImageIndex, imageWidthPixels, imageHeightPixels, imageWidthPoints, imageHeightPoints);
                    
                    // Create a page that EXACTLY matches the image dimensions in points (no margins, no scaling)
                    var pageSize = new iText.Kernel.Geom.PageSize(imageWidthPoints, imageHeightPoints);
                    var page = pdfDocument.AddNewPage(pageSize);
                    
                    // Create a canvas to draw directly on the page
                    var canvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page);
                    
                    // Add the image at position (0,0) with exact dimensions in points (no scaling, no margins)
                    // With DPI set to 96, this creates proper dimensions that Azure Translation Service
                    // processes only the visual content without any text that could be mistranslated
                    canvas.AddImageFittedIntoRectangle(imageData, 
                        new iText.Kernel.Geom.Rectangle(0, 0, imageWidthPoints, imageHeightPoints), false);
                    
                    // IMPORTANT: Do NOT add any text metadata to the images PDF
                    // All metadata (position, index, ID) is stored in the separate JSON metadata file
                    // The images PDF should contain ONLY images so Azure Translation Service
                    // processes only the visual content without any text that could be mistranslated
                    
                    _logger.LogDebug("Added image {Index} as full-page (no margins) {WidthPt:F1}x{HeightPt:F1} points at 96 DPI (should render at {WidthPx}x{HeightPx} pixels)", 
                        image.ImageIndex, imageWidthPoints, imageHeightPoints, imageWidthPixels, imageHeightPixels);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not add image {ImageName} to PDF", image.ImageName);
                }
            }

            pdfDocument.Close();
            outputStream.Position = 0;
            
            // Validate the created PDF dimensions before returning (only if enabled in config)
            if (_diagnosticSettings.EnablePdfDimensionValidation)
            {
                await ValidatePdfDimensions(outputStream, images);
                outputStream.Position = 0;
            }
            else
            {
                _logger.LogDebug("PDF dimension validation is disabled in configuration");
            }
            
            _logger.LogInformation("Successfully created PDF with {ImageCount} images with 96 DPI metadata for correct Azure rendering", images.Count);
            
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PDF from images");
            throw;
        }
    }

    /// <summary>
    /// Validates that the PDF page dimensions match the expected dimensions from the original images
    /// This helps catch dimension conversion issues before sending to Azure Translation Service
    /// </summary>
    private async Task ValidatePdfDimensions(Stream pdfStream, List<ExtractedImage> originalImages)
    {
        try
        {
            _logger.LogInformation("?? Validating PDF dimensions for {ImageCount} pages", originalImages.Count);
            
            pdfStream.Position = 0;
            using var pdfDocument = new PdfDocument(new PdfReader(pdfStream));
            
            var pageCount = pdfDocument.GetNumberOfPages();
            if (pageCount != originalImages.Count)
            {
                _logger.LogWarning("?? PDF page count ({PageCount}) does not match image count ({ImageCount})", 
                    pageCount, originalImages.Count);
            }
            
            var hasErrors = false;
            var validatedCount = 0;
            
            for (int i = 0; i < Math.Min(pageCount, originalImages.Count); i++)
            {
                var page = pdfDocument.GetPage(i + 1); // PDF pages are 1-indexed
                var originalImage = originalImages[i];
                
                var pageSize = page.GetPageSize();
                var actualWidthPoints = pageSize.GetWidth();
                var actualHeightPoints = pageSize.GetHeight();
                
                // Calculate expected dimensions based on original image pixels at 96 DPI
                var expectedWidthPoints = originalImage.Width * 0.75f;
                var expectedHeightPoints = originalImage.Height * 0.75f;
                
                // Allow 1 point tolerance for rounding errors
                var widthDiff = Math.Abs(actualWidthPoints - expectedWidthPoints);
                var heightDiff = Math.Abs(actualHeightPoints - expectedHeightPoints);
                
                if (widthDiff > 1.0f || heightDiff > 1.0f)
                {
                    _logger.LogError("? Page {PageNum} dimension mismatch for image {ImageId}:", 
                        i + 1, originalImage.ImageId);
                    _logger.LogError("   Expected: {ExpW:F1}x{ExpH:F1} points (from {OrigW}x{OrigH} pixels at 96 DPI)", 
                        expectedWidthPoints, expectedHeightPoints, originalImage.Width, originalImage.Height);
                    _logger.LogError("   Actual:   {ActW:F1}x{ActH:F1} points", 
                        actualWidthPoints, actualHeightPoints);
                    _logger.LogError("   Difference: {DiffW:F1}x{DiffH:F1} points", 
                        widthDiff, heightDiff);
                    
                    // Calculate what Azure will render this as at 96 DPI
                    var azureRenderWidth = (int)(actualWidthPoints * 96 / 72);
                    var azureRenderHeight = (int)(actualHeightPoints * 96 / 72);
                    _logger.LogError("   ?? Azure will render at: {AzureW}x{AzureH} pixels (expected: {ExpW}x{ExpH})", 
                        azureRenderWidth, azureRenderHeight, originalImage.Width, originalImage.Height);
                    _logger.LogError("   ?? Scaling factor: {ScaleW:F2}x{ScaleH:F2} (1.00 = no scaling needed)", 
                        (float)azureRenderWidth / originalImage.Width,
                        (float)azureRenderHeight / originalImage.Height);
                    
                    hasErrors = true;
                }
                else
                {
                    validatedCount++;
                    _logger.LogDebug("? Page {PageNum} dimensions correct: {W:F1}x{H:F1} points ({OrigW}x{OrigH} pixels at 96 DPI)", 
                        i + 1, actualWidthPoints, actualHeightPoints, originalImage.Width, originalImage.Height);
                }
            }
            
            if (hasErrors)
            {
                _logger.LogError("? PDF VALIDATION FAILED - {ErrorCount}/{TotalCount} pages have incorrect dimensions", 
                    Math.Min(pageCount, originalImages.Count) - validatedCount,
                    Math.Min(pageCount, originalImages.Count));
                _logger.LogError("   This will cause Azure Translation Service to scale images during rendering!");
            }
            else
            {
                _logger.LogInformation("? PDF VALIDATION SUCCESSFUL - all {Count} pages have correct dimensions for 96 DPI rendering", 
                    validatedCount);
                _logger.LogInformation("   Azure Translation Service should render images at original pixel dimensions without scaling");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not validate PDF dimensions - proceeding anyway");
        }
    }

    public async Task<Stream> ReplaceImagesInWordDocumentAsync(
        Stream originalWordStream,
        Stream translatedWordStream,
        List<ExtractedImage> translatedImages)
    {
        try
        {
            _logger.LogInformation("Replacing {ImageCount} images in Word document with position tracking", 
                translatedImages.Count);

            var outputStream = new MemoryStream();
            await translatedWordStream.CopyToAsync(outputStream);
            outputStream.Position = 0;

            using var wordDocument = WordprocessingDocument.Open(outputStream, true);
            var mainPart = wordDocument.MainDocumentPart;
            
            if (mainPart == null)
            {
                _logger.LogWarning("No main document part found");
                outputStream.Position = 0;
                return outputStream;
            }

            // Build a map of relationship IDs to image parts in the translated document
            var relationshipToPartMap = new Dictionary<string, ImagePart>();
            foreach (var imagePart in mainPart.ImageParts)
            {
                var relId = mainPart.GetIdOfPart(imagePart);
                relationshipToPartMap[relId] = imagePart;
            }

            var replacedCount = 0;
            var skippedCount = 0;
            
            // Replace images using their relationship IDs
            foreach (var translatedImage in translatedImages.OrderBy(i => i.ImageIndex))
            {
                // Skip replacement if the image has no text (no translation occurred)
                if (!translatedImage.HasText)
                {
                    _logger.LogInformation("Skipping image {Index} (relationship {RelId}) - no text detected, keeping original", 
                        translatedImage.ImageIndex, translatedImage.RelationshipId);
                    skippedCount++;
                    continue;
                }
                
                if (string.IsNullOrEmpty(translatedImage.RelationshipId))
                {
                    _logger.LogWarning("Image {ImageId} has no relationship ID, skipping", translatedImage.ImageId);
                    continue;
                }

                // Try to find the matching image part by relationship ID
                if (relationshipToPartMap.TryGetValue(translatedImage.RelationshipId, out var imagePart))
                {
                    try
                    {
                        // Replace the image data
                        using var imageStream = imagePart.GetStream(FileMode.Create);
                        await imageStream.WriteAsync(translatedImage.ImageData);
                        
                        replacedCount++;
                        _logger.LogInformation("Replaced image at position {Index} with relationship ID {RelId}", 
                            translatedImage.ImageIndex, translatedImage.RelationshipId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error replacing image {ImageId}", translatedImage.ImageId);
                    }
                }
                else
                {
                    _logger.LogWarning("Could not find image part for relationship ID {RelId}", 
                        translatedImage.RelationshipId);
                }
            }

            wordDocument.Save();
            outputStream.Position = 0;
            
            _logger.LogInformation("Successfully replaced {ReplacedCount}/{TotalCount} images in Word document ({SkippedCount} skipped - no text)", 
                replacedCount, translatedImages.Count, skippedCount);
            
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing images in Word document");
            throw;
        }
    }

    public async Task<Stream> ReplaceImagesInPdfAsync(
        Stream originalPdfStream,
        Stream translatedPdfStream,
        List<ExtractedImage> translatedImages)
    {
        // Use Python service if enabled and available
        if (_usePythonForPdf && _pythonPdfService != null)
        {
            try
            {
                _logger.LogInformation("Using Python service for PDF image replacement");
                return await _pythonPdfService.ReplaceImagesInPdfAsync(
                    translatedPdfStream,
                    translatedImages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Python PDF service failed, falling back to iText7 implementation");
                // Fall through to iText7 implementation
            }
        }

        // iText7-based implementation
        _logger.LogInformation("Using iText7 for PDF image replacement with {ImageCount} translated images", 
            translatedImages.Count);
        
        try
        {
            var outputStream = new MemoryStream();
            
            // Create a memory stream for the translated PDF to read from
            var translatedPdfMemory = new MemoryStream();
            await translatedPdfStream.CopyToAsync(translatedPdfMemory);
            translatedPdfMemory.Position = 0;

            using var pdfReader = new PdfReader(translatedPdfMemory);
            using var pdfWriter = new PdfWriter(outputStream);
            pdfWriter.SetCloseStream(false);
            using var pdfDocument = new PdfDocument(pdfReader, pdfWriter);

            var replacedCount = 0;

            // Group images by page number for efficient processing
            var imagesByPage = translatedImages
                .Where(img => img.PageNumber > 0)
                .GroupBy(img => img.PageNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            _logger.LogInformation("Processing {PageCount} pages for image replacement", imagesByPage.Count);

            // Iterate through each page that has images
            foreach (var pageNum in imagesByPage.Keys.OrderBy(p => p))
            {
                if (pageNum < 1 || pageNum > pdfDocument.GetNumberOfPages())
                {
                    _logger.LogWarning("Page number {PageNum} is out of range (1-{MaxPage})", 
                        pageNum, pdfDocument.GetNumberOfPages());
                    continue;
                }

                var page = pdfDocument.GetPage(pageNum);
                var resources = page.GetResources();
                
                if (resources == null)
                {
                    _logger.LogWarning("No resources found on page {PageNum}", pageNum);
                    continue;
                }

                var pageImages = imagesByPage[pageNum];
                _logger.LogInformation("Processing page {PageNum} with {ImageCount} images to replace", 
                    pageNum, pageImages.Count);

                // Get all XObject names from the page
                var xObjectNames = resources.GetResourceNames();
                if (xObjectNames == null || !xObjectNames.Any())
                {
                    _logger.LogWarning("No XObjects found on page {PageNum}", pageNum);
                    continue;
                }

                // Try to match and replace each translated image
                foreach (var translatedImage in pageImages)
                {
                    try
                    {
                        // Skip replacement if the image has no text (no translation occurred)
                        if (!translatedImage.HasText)
                        {
                            _logger.LogInformation("Skipping image {Index} on page {PageNum} - no text detected, keeping original", 
                                translatedImage.ImageIndex, pageNum);
                            continue;
                        }
                        
                        // Use the RelationshipId (XObject name) from the extracted image metadata
                        // This is the actual name of the image resource in the PDF
                        if (string.IsNullOrEmpty(translatedImage.RelationshipId))
                        {
                            _logger.LogWarning("Image {Index} on page {PageNum} has no RelationshipId, cannot replace", 
                                translatedImage.ImageIndex, pageNum);
                            continue;
                        }

                        // Parse the XObject name from the RelationshipId
                        // RelationshipId format is like "/Im1" or "Im1"
                        var xObjectNameStr = translatedImage.RelationshipId.TrimStart('/');
                        var xObjectName = new iText.Kernel.Pdf.PdfName(xObjectNameStr);
                        
                        // Verify this XObject exists on the page
                        if (!xObjectNames.Contains(xObjectName))
                        {
                            _logger.LogWarning("XObject {Name} not found on page {PageNum} (image index {Index})", 
                                xObjectNameStr, pageNum, translatedImage.ImageIndex);
                            continue;
                        }

                        // Get the existing XObject
                        var xObject = resources.GetResourceObject(iText.Kernel.Pdf.PdfName.XObject, xObjectName);
                        if (xObject == null)
                        {
                            _logger.LogWarning("XObject {Name} is null on page {PageNum}", xObjectName, pageNum);
                            continue;
                        }

                        // Resolve indirect reference if needed
                        var pdfObject = xObject.IsIndirectReference() 
                            ? ((iText.Kernel.Pdf.PdfIndirectReference)xObject).GetRefersTo() 
                            : xObject;

                        if (pdfObject is not PdfStream stream)
                        {
                            _logger.LogWarning("XObject {Name} is not a PdfStream on page {PageNum}", xObjectName, pageNum);
                            continue;
                        }

                        var subType = stream.GetAsName(iText.Kernel.Pdf.PdfName.Subtype);
                        if (subType == null || !subType.Equals(iText.Kernel.Pdf.PdfName.Image))
                        {
                            _logger.LogWarning("XObject {Name} is not an image on page {PageNum}", xObjectName, pageNum);
                            continue;
                        }

                        // Create new image from translated image data
                        var imageDataObj = iText.IO.Image.ImageDataFactory.Create(translatedImage.ImageData);
                        var newPdfImageXObject = new PdfImageXObject(imageDataObj);

                        // Get original dimensions
                        var originalXObject = new PdfImageXObject(stream);
                        var originalWidth = originalXObject.GetWidth();
                        var originalHeight = originalXObject.GetHeight();

                        _logger.LogInformation("Replacing image at page {PageNum}, index {Index}: " +
                            "original dimensions {OrigW}x{OrigH}, new dimensions {NewW}x{NewH}",
                            pageNum, translatedImage.ImageIndex, 
                            originalWidth, originalHeight,
                            newPdfImageXObject.GetWidth(), newPdfImageXObject.GetHeight());

                        // Replace the XObject in the resources dictionary
                        // This updates the reference for all uses of this image on the page
                        var xObjectDict = resources.GetPdfObject().GetAsDictionary(iText.Kernel.Pdf.PdfName.XObject);
                        if (xObjectDict != null)
                        {
                            // Put the new image XObject with the same name, replacing the old one
                            xObjectDict.Put(xObjectName, newPdfImageXObject.GetPdfObject());
                            page.SetModified();
                        }

                        replacedCount++;
                        _logger.LogInformation("Successfully replaced image {Index} on page {PageNum}", 
                            translatedImage.ImageIndex, pageNum);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error replacing image {Index} on page {PageNum}", 
                            translatedImage.ImageIndex, pageNum);
                        // Continue with other images even if one fails
                    }
                }
            }

            pdfDocument.Close();
            outputStream.Position = 0;

            var skippedCount = translatedImages.Count(img => !img.HasText);
            _logger.LogInformation("Successfully replaced {ReplacedCount} of {TotalCount} images in PDF ({SkippedCount} skipped - no text)", 
                replacedCount, translatedImages.Count, skippedCount);

            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PDF image replacement using iText7");
            
            // Return the translated PDF without modifications as fallback
            _logger.LogWarning("Returning translated PDF without image replacement due to error");
            var fallbackStream = new MemoryStream();
            translatedPdfStream.Position = 0;
            await translatedPdfStream.CopyToAsync(fallbackStream);
            fallbackStream.Position = 0;
            return fallbackStream;
        }
    }

    public async Task<Stream> ReplaceImagesInPowerPointAsync(
        Stream originalPptxStream,
        Stream translatedPptxStream,
        List<ExtractedImage> translatedImages)
    {
        try
        {
            _logger.LogInformation("Replacing {ImageCount} images in PowerPoint with position tracking", 
                translatedImages.Count);

            // Log z-order information from metadata
            var imagesWithZOrder = translatedImages.Where(img => img.Position?.ZOrder != null).ToList();
            if (imagesWithZOrder.Any())
            {
                _logger.LogInformation("Found {Count} images with z-order metadata:", imagesWithZOrder.Count);
                foreach (var img in imagesWithZOrder.OrderBy(i => i.Position!.ZOrder))
                {
                    _logger.LogInformation("  ? {ImageId}: Z-Order {ZOrder} (RelId: {RelId})", 
                        img.ImageId, img.Position.ZOrder, img.RelationshipId);
                }
            }
            else
            {
                _logger.LogWarning("No images have z-order metadata - layering may not be preserved");
            }

            var outputStream = new MemoryStream();
            await translatedPptxStream.CopyToAsync(outputStream);
            outputStream.Position = 0;

            using var presentationDocument = PresentationDocument.Open(outputStream, true);
            var presentationPart = presentationDocument.PresentationPart;
            
            if (presentationPart == null)
            {
                _logger.LogWarning("No presentation part found");
                outputStream.Position = 0;
                return outputStream;
            }

            // Group translated images by slide number for efficient processing
            var imagesBySlide = translatedImages
                .Where(img => img.PageNumber > 0) // PageNumber is used as slide number
                .GroupBy(img => img.PageNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            var replacedCount = 0;
            var skippedCount = 0;
            
            _logger.LogInformation("Processing {SlideCount} slides for image replacement", imagesBySlide.Count);

            // Get all slides
            var slideIds = presentationPart.Presentation?.SlideIdList?.Elements<P.SlideId>();
            if (slideIds == null || !slideIds.Any())
            {
                _logger.LogWarning("No slides found in PowerPoint");
                outputStream.Position = 0;
                return outputStream;
            }

            var slideIndex = 0;
            foreach (var slideId in slideIds)
            {
                slideIndex++;
                
                // Skip slides that don't have images to replace
                if (!imagesBySlide.ContainsKey(slideIndex))
                {
                    continue;
                }

                var slidePart = presentationPart.GetPartById(slideId.RelationshipId!) as SlidePart;
                if (slidePart == null)
                {
                    _logger.LogWarning("Could not load slide {SlideIndex}", slideIndex);
                    continue;
                }

                // Build a map of relationship IDs to image parts for this slide
                var relationshipToPartMap = new Dictionary<string, ImagePart>();
                foreach (var imagePart in slidePart.ImageParts)
                {
                    var relId = slidePart.GetIdOfPart(imagePart);
                    relationshipToPartMap[relId] = imagePart;
                }

                var slideImages = imagesBySlide[slideIndex];
                _logger.LogInformation("Processing slide {SlideIndex} with {ImageCount} images to replace", 
                    slideIndex, slideImages.Count);

                // VERIFY z-order BEFORE replacement
                if (slidePart.Slide != null)
                {
                    var shapeTree = slidePart.Slide.CommonSlideData?.ShapeTree;
                    if (shapeTree != null)
                    {
                        var totalElements = shapeTree.ChildElements.Count;
                        var pictureElements = shapeTree.ChildElements.OfType<P.Picture>().Count();
                        var graphicFrameElements = shapeTree.ChildElements.OfType<P.GraphicFrame>().Count();
                        
                        _logger.LogInformation("BEFORE replacement - Slide {SlideIndex} shape tree: {TotalElements} total, {PictureElements} Picture, {GraphicFrameElements} GraphicFrame", 
                            slideIndex, totalElements, pictureElements, graphicFrameElements);
                        
                        int currentZOrder = 0;
                        foreach (var element in shapeTree.ChildElements)
                        {
                            _logger.LogInformation("  Element {Index}: Type = {Type}", 
                                currentZOrder, element.GetType().Name);
                            
                            if (element is P.Picture picture)
                            {
                                var blip = picture.Descendants<A.Blip>().FirstOrDefault();
                                if (blip?.Embed?.Value != null)
                                {
                                    _logger.LogInformation("    ? Picture with RelId {RelId} at position {ZOrder}", 
                                        blip.Embed.Value, currentZOrder);
                                }
                                else
                                {
                                    _logger.LogWarning("    ? Picture has NO Blip or Embed value!");
                                }
                            }
                            else if (element is P.GraphicFrame graphicFrame)
                            {
                                var blip = graphicFrame.Descendants<A.Blip>().FirstOrDefault();
                                if (blip?.Embed?.Value != null)
                                {
                                    _logger.LogInformation("    ? GraphicFrame (Visio/Object) with RelId {RelId} at position {ZOrder}", 
                                        blip.Embed.Value, currentZOrder);
                                }
                                else
                                {
                                    _logger.LogInformation("    ? GraphicFrame (no image reference - might be chart/table)");
                                }
                            }
                            currentZOrder++;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Slide {SlideIndex}: No shape tree found - z-order cannot be captured", slideIndex);
                    }
                }

                // Replace images using their relationship IDs
                foreach (var translatedImage in slideImages.OrderBy(i => i.ImageIndex))
                {
                    // Skip replacement if the image has no text (no translation occurred)
                    if (!translatedImage.HasText)
                    {
                        _logger.LogInformation("  ? Skipping image {Index} (RelId: {RelId}) - no text detected, keeping original", 
                            translatedImage.ImageIndex, translatedImage.RelationshipId);
                        skippedCount++;
                        continue;
                    }
                    
                    if (string.IsNullOrEmpty(translatedImage.RelationshipId))
                    {
                        _logger.LogWarning("  ? Image {ImageId} has no relationship ID, skipping", 
                            translatedImage.ImageId);
                        continue;
                    }

                    // Try to find the matching image part by relationship ID
                    if (relationshipToPartMap.TryGetValue(translatedImage.RelationshipId, out var imagePart))
                    {
                        try
                        {
                            var zOrderInfo = translatedImage.Position?.ZOrder != null 
                                ? $"Z-Order: {translatedImage.Position.ZOrder}" 
                                : "Z-Order: unknown";
                            
                            _logger.LogInformation("  ? Replacing image {Index} (RelId: {RelId}, {ZOrder})", 
                                translatedImage.ImageIndex, translatedImage.RelationshipId, zOrderInfo);
                            
                            // Replace the image data
                            // IMPORTANT: This preserves z-order because we're updating the same ImagePart
                            // The relationship ID stays the same, and the picture element stays in the same
                            // position in the shape tree, maintaining its z-order
                            using var imageStream = imagePart.GetStream(FileMode.Create);
                            await imageStream.WriteAsync(translatedImage.ImageData);
                        
                            replacedCount++;
                            _logger.LogInformation("  ? Replaced image data successfully - RelId {RelId} unchanged", 
                                translatedImage.RelationshipId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "  ? Error replacing image {ImageId}", 
                                translatedImage.ImageId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("  ? Could not find image part for relationship ID {RelId} on slide {SlideIndex}", 
                            translatedImage.RelationshipId, slideIndex);
                    }
                }

                // VERIFY z-order AFTER replacement
                if (slidePart.Slide != null)
                {
                    var shapeTree = slidePart.Slide.CommonSlideData?.ShapeTree;
                    if (shapeTree != null)
                    {
                        var totalElements = shapeTree.ChildElements.Count;
                        var pictureElements = shapeTree.ChildElements.OfType<P.Picture>().Count();
                        var graphicFrameElements = shapeTree.ChildElements.OfType<P.GraphicFrame>().Count();
                        
                        _logger.LogInformation("AFTER replacement - Slide {SlideIndex} shape tree: {TotalElements} total, {PictureElements} Picture, {GraphicFrameElements} GraphicFrame", 
                            slideIndex, totalElements, pictureElements, graphicFrameElements);
                        
                        int currentZOrder = 0;
                        bool zOrderChanged = false;
                        foreach (var element in shapeTree.ChildElements)
                        {
                            _logger.LogInformation("  Element {Index}: Type = {Type}", 
                                currentZOrder, element.GetType().Name);
                            
                            if (element is P.Picture picture)
                            {
                                var blip = picture.Descendants<A.Blip>().FirstOrDefault();
                                if (blip?.Embed?.Value != null)
                                {
                                    var relId = blip.Embed.Value;
                                    var expectedZOrder = slideImages
                                        .FirstOrDefault(img => img.RelationshipId == relId)
                                        ?.Position?.ZOrder;
                                    
                                    if (expectedZOrder.HasValue && expectedZOrder.Value != currentZOrder)
                                    {
                                        _logger.LogWarning("    ?? Picture with RelId {RelId} at position {ActualZOrder} - EXPECTED {ExpectedZOrder}!", 
                                            relId, currentZOrder, expectedZOrder.Value);
                                        zOrderChanged = true;
                                    }
                                    else
                                    {
                                        _logger.LogInformation("    ? Picture with RelId {RelId} at position {ZOrder} - correct", 
                                            relId, currentZOrder);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("    ? Picture has NO Blip or Embed value!");
                                }
                            }
                            else if (element is P.GraphicFrame graphicFrame)
                            {
                                var blip = graphicFrame.Descendants<A.Blip>().FirstOrDefault();
                                if (blip?.Embed?.Value != null)
                                {
                                    var relId = blip.Embed.Value;
                                    var expectedZOrder = slideImages
                                        .FirstOrDefault(img => img.RelationshipId == relId)
                                        ?.Position?.ZOrder;
                                    
                                    if (expectedZOrder.HasValue && expectedZOrder.Value != currentZOrder)
                                    {
                                        _logger.LogWarning("    ?? GraphicFrame with RelId {RelId} at position {ActualZOrder} - EXPECTED {ExpectedZOrder}!", 
                                            relId, currentZOrder, expectedZOrder.Value);
                                        zOrderChanged = true;
                                    }
                                    else
                                    {
                                        _logger.LogInformation("    ? GraphicFrame (Visio/Object) with RelId {RelId} at position {ZOrder} - correct", 
                                            relId, currentZOrder);
                                    }
                                }
                                else
                                {
                                    _logger.LogInformation("    ? GraphicFrame (no image reference - might be chart/table)");
                                }
                            }
                            currentZOrder++;
                        }
                        
                        var totalImageElements = pictureElements + graphicFrameElements;
                        if (zOrderChanged)
                        {
                            _logger.LogError("? Z-ORDER HAS CHANGED on slide {SlideIndex} - layering is NOT preserved!", slideIndex);
                        }
                        else if (totalImageElements > 0)
                        {
                            _logger.LogInformation("? Z-order verified - all {Count} images/objects in correct positions on slide {SlideIndex}", 
                                totalImageElements, slideIndex);
                        }
                        else
                        {
                            _logger.LogWarning("?? No Picture or GraphicFrame elements found for verification on slide {SlideIndex}", slideIndex);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("AFTER replacement - Slide {SlideIndex} has NO shape tree!", slideIndex);
                    }
                }
                else
                {
                    _logger.LogWarning("AFTER replacement - Slide {SlideIndex} has NULL Slide!", slideIndex);
                }
            }

            presentationDocument.Save();
            outputStream.Position = 0;
            
            _logger.LogInformation("? Successfully replaced {ReplacedCount}/{TotalCount} images in PowerPoint ({SkippedCount} skipped - no text)", 
                replacedCount, translatedImages.Count, skippedCount);
            _logger.LogInformation("Check logs above for z-order verification results");
            
            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing images in PowerPoint");
            throw;
        }
    }

    private string GetImageFormat(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/tiff" => "tiff",
            _ => "png"
        };
    }
    
    /// <summary>
    /// Converts EMF/WMF (Windows Metafile) images to PNG format.
    /// On Windows: Uses native GDI+ (System.Drawing) for perfect rendering at native resolution
    /// On Linux: Uses ImageMagick with fallback to white placeholder
    /// </summary>
    private byte[] ConvertEmfWmfToPng(byte[] metafileData, int displayWidth, int displayHeight, string contentType)
    {
        _logger.LogInformation("Converting {ContentType} to PNG (display dimensions: {Width}x{Height})", 
            contentType, displayWidth, displayHeight);
        
        // Try native Windows GDI+ first (only available on Windows)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _logger.LogInformation("Using native Windows GDI+ for EMF/WMF conversion");
                
                using var ms = new MemoryStream(metafileData);
                using var metafile = System.Drawing.Image.FromStream(ms);
                
                // DIAGNOSTIC: Log ALL metafile properties for debugging
                _logger.LogDebug("=== METAFILE DIAGNOSTIC INFO ===");
                _logger.LogDebug("Raw metafile.Width: {Width}", metafile.Width);
                _logger.LogDebug("Raw metafile.Height: {Height}", metafile.Height);
                _logger.LogDebug("HorizontalResolution (DPI): {HRes}", metafile.HorizontalResolution);
                _logger.LogDebug("VerticalResolution (DPI): {VRes}", metafile.VerticalResolution);
                _logger.LogDebug("PixelFormat: {Format}", metafile.PixelFormat);
                _logger.LogDebug("RawFormat: {Format}", metafile.RawFormat);
                _logger.LogDebug("PhysicalDimension.Width: {Width}", metafile.PhysicalDimension.Width);
                _logger.LogDebug("PhysicalDimension.Height: {Height}", metafile.PhysicalDimension.Height);
                
                // Calculate various resolution interpretations
                float widthInches = metafile.Width / metafile.HorizontalResolution;
                float heightInches = metafile.Height / metafile.VerticalResolution;
                _logger.LogDebug("Physical size in inches: {Width:F2} x {Height:F2}", widthInches, heightInches);
                
                // Try different DPI calculations
                float maxDpi = Math.Max(metafile.HorizontalResolution, metafile.VerticalResolution);
                int option1Width = (int)(widthInches * maxDpi);
                int option1Height = (int)(heightInches * maxDpi);
                _logger.LogDebug("Option 1 (inches * maxDPI={DPI}): {Width}x{Height}", maxDpi, option1Width, option1Height);
                
                int option2Width = (int)(widthInches * 96);
                int option2Height = (int)(heightInches * 96);
                _logger.LogDebug("Option 2 (inches * 96 DPI): {Width}x{Height}", option2Width, option2Height);
                
                int option3Width = (int)(widthInches * 300);
                int option3Height = (int)(heightInches * 300);
                _logger.LogDebug("Option 3 (inches * 300 DPI): {Width}x{Height}", option3Width, option3Height);
                
                int option4Width = metafile.Width;
                int option4Height = metafile.Height;
                _logger.LogDebug("Option 4 (raw Width/Height): {Width}x{Height}", option4Width, option4Height);
                
                int option5Width = (int)metafile.PhysicalDimension.Width;
                int option5Height = (int)metafile.PhysicalDimension.Height;
                _logger.LogDebug("Option 5 (PhysicalDimension): {Width}x{Height}", option5Width, option5Height);
                
                // If it's a metafile, try to get the enhanced metafile header
                if (metafile is System.Drawing.Imaging.Metafile emfDiag)
                {
                    var header = emfDiag.GetMetafileHeader();
                    _logger.LogDebug("Metafile Type: {Type}", header.Type);
                    _logger.LogDebug("Metafile Bounds: {Bounds}", header.Bounds);
                    _logger.LogDebug("Metafile DpiX: {DpiX}, DpiY: {DpiY}", header.DpiX, header.DpiY);
                    _logger.LogDebug("Metafile MetafileSize: {Size}", header.MetafileSize);
                    
                    // Try using metafile header DPI
                    int option6Width = (int)(widthInches * header.DpiX);
                    int option6Height = (int)(heightInches * header.DpiY);
                    _logger.LogDebug("Option 6 (inches * header DPI={DpiX}x{DpiY}): {Width}x{Height}", 
                        header.DpiX, header.DpiY, option6Width, option6Height);
                    
                    // Try using bounds
                    int option7Width = (int)header.Bounds.Width;
                    int option7Height = (int)header.Bounds.Height;
                    _logger.LogDebug("Option 7 (header.Bounds): {Width}x{Height}", option7Width, option7Height);
                    
                    // Try using PhysicalDimension with different scaling factors
                    // PhysicalDimension is often in .01mm units (1/100th of a millimeter)
                    int option8Width = (int)(metafile.PhysicalDimension.Width / 5.38f);
                    int option8Height = (int)(metafile.PhysicalDimension.Height / 5.38f);
                    _logger.LogDebug("Option 8 (PhysicalDimension / 5.38): {Width}x{Height}", option8Width, option8Height);
                    
                    // Try calculating what DPI would give us the PhysicalDimension as pixels
                    float physDpiX = metafile.PhysicalDimension.Width / widthInches;
                    float physDpiY = metafile.PhysicalDimension.Height / heightInches;
                    int option9Width = (int)(widthInches * physDpiX);
                    int option9Height = (int)(heightInches * physDpiY);
                    _logger.LogDebug("Option 9 (inches * PhysicalDimension-derived DPI={DpiX:F1}x{DpiY:F1}): {Width}x{Height}", 
                        physDpiX, physDpiY, option9Width, option9Height);
                }
                
                _logger.LogDebug("================================");
                
                // IMPORTANT: Metafiles are vector graphics that need proper resolution calculation
                // The Width/Height properties might be in different units (inches, millimeters, etc.)
                // We need to calculate the actual pixel dimensions using DPI
                
                // SOLUTION: Use PhysicalDimension which contains the true high-resolution pixel dimensions
                // PhysicalDimension is in .01mm units (1/100th of a millimeter), so we need to scale it
                // The scaling factor 5.38 converts from .01mm to pixels at the original image resolution
                int nativeWidth;
                int nativeHeight;
                
                if (metafile is System.Drawing.Imaging.Metafile emfConvert)
                {
                    // Use PhysicalDimension with empirically determined scaling factor
                    // This preserves the original high-resolution dimensions of the embedded image
                    nativeWidth = (int)(metafile.PhysicalDimension.Width / 5.38f);
                    nativeHeight = (int)(metafile.PhysicalDimension.Height / 5.38f);
                    
                    _logger.LogInformation("Extracted native resolution from PhysicalDimension: {Width}x{Height}", 
                        nativeWidth, nativeHeight);
                }
                else
                {
                    // Fallback for non-EMF metafiles
                    float nativeDpi = Math.Max(metafile.HorizontalResolution, metafile.VerticalResolution);
                    nativeWidth = (int)(widthInches * nativeDpi);
                    nativeHeight = (int)(heightInches * nativeDpi);
                    
                    _logger.LogInformation("Calculated native resolution from DPI: {Width}x{Height}", 
                        nativeWidth, nativeHeight);
                }
                
                // Ensure we have valid dimensions
                if (nativeWidth <= 0 || nativeHeight <= 0)
                {
                    _logger.LogWarning("Calculated invalid dimensions {W}x{H}, falling back to metafile.Width x metafile.Height", 
                        nativeWidth, nativeHeight);
                    nativeWidth = metafile.Width;
                    nativeHeight = metafile.Height;
                }
                
                _logger.LogDebug("SELECTED: Rendering metafile at {NativeW}x{NativeH} (display was {DispW}x{DispH})", 
                    nativeWidth, nativeHeight, displayWidth, displayHeight);
                
                // Create a bitmap at NATIVE resolution to preserve quality
                using var bitmap = new System.Drawing.Bitmap(nativeWidth, nativeHeight);
                // Use the metafile's DPI for proper rendering
                bitmap.SetResolution(metafile.HorizontalResolution, metafile.VerticalResolution);
                
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                
                // Set high quality rendering
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                
                // Fill with white background
                graphics.Clear(System.Drawing.Color.White);
                
                // Draw the metafile at its NATIVE resolution
                graphics.DrawImage(metafile, 0, 0, nativeWidth, nativeHeight);
                
                // Convert to PNG bytes
                using var outputStream = new MemoryStream();
                bitmap.Save(outputStream, System.Drawing.Imaging.ImageFormat.Png);
                var pngData = outputStream.ToArray();
                
                _logger.LogInformation("Successfully converted {ContentType} to PNG at native resolution using Windows GDI+ ({Size} bytes, {Width}x{Height})", 
                    contentType, pngData.Length, nativeWidth, nativeHeight);
                
                return pngData;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Windows GDI+ conversion failed for {ContentType}, trying ImageMagick fallback", contentType);
                // Fall through to ImageMagick attempt
            }
        }
        
        // Try ImageMagick (Linux or Windows fallback)
        try
        {
            _logger.LogInformation("Using ImageMagick for {ContentType} conversion", contentType);
            
            using var magickImage = new MagickImage(metafileData);
            
            // ImageMagick should automatically use native resolution
            var nativeWidth = (int)magickImage.Width;
            var nativeHeight = (int)magickImage.Height;
            
            _logger.LogInformation("ImageMagick detected resolution: {NativeW}x{NativeH} (display was {DispW}x{DispH})", 
                nativeWidth, nativeHeight, displayWidth, displayHeight);
            
            // Set output format to PNG
            magickImage.Format = MagickFormat.Png;
            
            // DO NOT resize - keep native resolution for quality
            // The old code was: if (magickImage.Width != displayWidth || magickImage.Height != displayHeight) { magickImage.Resize(...) }
            // This downscaled the image! We want to keep the native resolution.
            
            // Set white background (in case of transparency)
            magickImage.BackgroundColor = MagickColors.White;
            magickImage.Alpha(AlphaOption.Remove);
            
            // Convert to PNG bytes at native resolution
            var pngData = magickImage.ToByteArray();
            
            _logger.LogInformation("Successfully converted {ContentType} to PNG at native resolution using ImageMagick ({Size} bytes, {Width}x{Height})", 
                contentType, pngData.Length, nativeWidth, nativeHeight);
            
            return pngData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ImageMagick failed to convert {ContentType}, creating placeholder at display dimensions", contentType);
            
            // Final fallback: Create a white PNG placeholder using ImageSharp at display dimensions
            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(displayWidth, displayHeight);
            image.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.White));
            
            using var outputMs = new MemoryStream();
            image.SaveAsPng(outputMs);
            
            var placeholderData = outputMs.ToArray();
            _logger.LogWarning("Created white placeholder PNG ({Size} bytes, {Width}x{Height}) - original {ContentType} could not be converted", 
                placeholderData.Length, displayWidth, displayHeight, contentType);
            
            return placeholderData;
        }
    }
}
