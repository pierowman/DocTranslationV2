using Microsoft.AspNetCore.Http;

namespace DocTranslationV2.Models;

public class TranslationRequest
{
    public List<IFormFile> Files { get; set; } = new();
    public string? SourceLanguage { get; set; }
    public List<string> TargetLanguages { get; set; } = new();
    public bool UseAsyncProcessing { get; set; }
    public bool AutoDetectLanguage { get; set; }
    public bool ProcessImages { get; set; } = false; // Changed: Default to disabled
    
    // Image filtering options (passed from UI or defaults from config)
    public ImageFilteringOptions? ImageFiltering { get; set; }
}

/// <summary>
/// View model for the translation form (accepts form data from UI)
/// </summary>
public class TranslationRequestViewModel
{
    public List<IFormFile> Files { get; set; } = new();
    public string? SourceLanguage { get; set; }
    public List<string> TargetLanguages { get; set; } = new();
    public bool UseAsyncProcessing { get; set; }
    public bool AutoDetectLanguage { get; set; }
    public bool ProcessImages { get; set; } = false;
    
    // Image filtering options from UI
    public bool FilterImagesWithContainedText { get; set; } = true;
    public bool FilterDecorativeImages { get; set; } = true;
}

/// <summary>
/// Image filtering options for a specific translation request
/// </summary>
public class ImageFilteringOptions
{
    public bool FilterImagesWithContainedText { get; set; }
    public bool FilterDecorativeImages { get; set; }
    public int MinimumImageSizeBytes { get; set; }
    public int MinimumImageWidthPixels { get; set; }
    public int MinimumImageHeightPixels { get; set; }
}

public class TranslationResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<TranslatedFile> TranslatedFiles { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsAsync { get; set; }
    public string CurrentPhase { get; set; } = string.Empty; // Initial phase when job is created
}

public class TranslatedFile
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string TargetLanguageName { get; set; } = string.Empty;
    public string TranslatedBlobUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}

public class JobStatus
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public int TranslatedDocuments { get; set; }
    public int FailedDocuments { get; set; }
    public int DocumentsInProgress { get; set; }
    public int DocumentsNotStarted { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<TranslatedFile> TranslatedFiles { get; set; } = new();
    
    // New detailed status fields
    public string DetailedStatus { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public List<string> TargetLanguages { get; set; } = new();
    public DateTimeOffset? CreatedOn { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public TimeSpan? ElapsedTime { get; set; }
    public string CurrentPhase { get; set; } = string.Empty; // e.g., "Uploading", "Translating", "Completed"
}

public class SupportedLanguage
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
}

public class TranslationJobInfo
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public int TotalDocuments { get; set; }
    public int DocumentsSucceeded { get; set; }
    public int DocumentsFailed { get; set; }
    public int DocumentsInProgress { get; set; }
    public int DocumentsNotStarted { get; set; }
    public int DocumentsCanceled { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
