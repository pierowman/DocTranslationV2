namespace DocTranslationV2.Models;
public class DocumentImageInfo
{
    public string OriginalFilePath { get; set; } = string.Empty;
    public List<ExtractedImage> Images { get; set; } = new();
    public bool HasImages { get; set; }
    public bool HasTextContent { get; set; } = true; // Default to true for safety
    public string DocumentType { get; set; } = string.Empty; // "pdf" or "docx"
}

public class ExtractedImage
{
    public int PageNumber { get; set; }
    public int ImageIndex { get; set; } // Index within the document
    public string ImageName { get; set; } = string.Empty;
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public string Format { get; set; } = string.Empty;
    
    // Position tracking for proper replacement
    public string ImageId { get; set; } = string.Empty; // Unique identifier
    public string RelationshipId { get; set; } = string.Empty; // For Word documents
    public ImagePosition? Position { get; set; }
    
    // Metadata for verification
    public long OriginalSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    
    // Text detection for optimization
    public bool HasText { get; set; } = true; // Default to true to preserve original behavior (always replace)
}

public class ImagePosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string PositionType { get; set; } = string.Empty; // "inline", "floating", etc.
    
    /// <summary>
    /// Z-order (layering) of the image. Lower numbers are behind, higher numbers are in front.
    /// Used in PowerPoint to preserve whether images should be at the back or front of other objects.
    /// Null for formats that don't support explicit z-ordering.
    /// </summary>
    public int? ZOrder { get; set; }
}

public class ImageTranslationResult
{
    public string OriginalDocumentPath { get; set; } = string.Empty;
    public string TranslatedDocumentPath { get; set; } = string.Empty;
    public string ImagesPdfPath { get; set; } = string.Empty;
    public string TranslatedImagesPdfPath { get; set; } = string.Empty;
    public List<ExtractedImage> OriginalImages { get; set; } = new();
    public List<ImageMapping> ImageMappings { get; set; } = new(); // Track original -> translated mapping
}

public class ImageMapping
{
    public string OriginalImageId { get; set; } = string.Empty;
    public string TranslatedImagePath { get; set; } = string.Empty;
    public int OriginalIndex { get; set; }
    public int TranslatedIndex { get; set; }
    public bool ReplacementSuccessful { get; set; }
}
