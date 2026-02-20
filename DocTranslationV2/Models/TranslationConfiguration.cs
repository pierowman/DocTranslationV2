namespace DocTranslationV2.Models;

public class TranslationConfiguration
{
    public AzureTranslationSettings AzureTranslation { get; set; } = new();
    public AzureBlobStorageSettings AzureBlobStorage { get; set; } = new();
    public ImageFilteringSettings ImageFiltering { get; set; } = new();
    public DiagnosticSettings Diagnostics { get; set; } = new();
}

public class AzureTranslationSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string SubscriptionKey { get; set; } = string.Empty;
    public string LanguageApiUrl { get; set; } = "https://api.cognitive.microsofttranslator.com/languages?api-version=3.0&scope=translation";
    public int LanguageCacheExpirationMinutes { get; set; } = 1440; // 24 hours default
    public SupportedFileTypes SupportedFileTypes { get; set; } = new();
}

public class SupportedFileTypes
{
    /// <summary>
    /// File types supported by batch (async) translation
    /// </summary>
    public List<string> Batch { get; set; } = new()
    {
        ".pdf", ".docx", ".pptx", ".xlsx",
        ".txt", ".html", ".htm", ".rtf",
        ".odt", ".ods", ".odp"
    };

    /// <summary>
    /// File types supported by single document (sync) translation
    /// Note: Azure SingleDocumentTranslationClient supports fewer formats
    /// </summary>
    public List<string> Sync { get; set; } = new()
    {
        ".pdf", ".docx", ".pptx",
        ".txt", ".html", ".htm"
    };

    /// <summary>
    /// File types that support image extraction and processing
    /// Only available in batch mode
    /// </summary>
    public List<string> ImageProcessingSupported { get; set; } = new()
    {
        ".pdf", ".docx", ".pptx"
    };
}

public class ImageFilteringSettings
{
    /// <summary>
    /// Enable filtering of images with text contained within their boundaries
    /// (e.g., styled titles with colored backgrounds)
    /// </summary>
    public bool FilterImagesWithContainedText { get; set; } = true;

    /// <summary>
    /// Enable filtering of likely decorative images
    /// (e.g., borders, backgrounds, solid colors, very small images)
    /// </summary>
    public bool FilterDecorativeImages { get; set; } = true;

    /// <summary>
    /// Minimum image size in bytes to process (smaller images are filtered)
    /// </summary>
    public int MinimumImageSizeBytes { get; set; } = 100;

    /// <summary>
    /// Minimum image width in pixels (smaller images are filtered)
    /// </summary>
    public int MinimumImageWidthPixels { get; set; } = 32;

    /// <summary>
    /// Minimum image height in pixels (smaller images are filtered)
    /// </summary>
    public int MinimumImageHeightPixels { get; set; } = 32;
}

public class DiagnosticSettings
{
    /// <summary>
    /// Enable uploading extracted images to a diagnostic blob container for inspection.
    /// Useful for debugging image extraction and dimension issues.
    /// Should be enabled only in Development environment.
    /// </summary>
    public bool EnableImageUpload { get; set; } = false;

    /// <summary>
    /// Enable detailed dimension validation logging for images in the generated PDF.
    /// Helps identify scaling issues before sending to Azure Translation Service.
    /// </summary>
    public bool EnablePdfDimensionValidation { get; set; } = false;
}

public class AzureBlobStorageSettings
{
    public string AccountName { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "translations";
}
