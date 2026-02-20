namespace DocTranslationV2.Constants;

/// <summary>
/// Constants for Azure Blob Storage container naming patterns
/// </summary>
public static class ContainerNamePatterns
{
    public const string JobSourcePattern = "job-{0}-source";
    public const string JobTargetPattern = "job-{0}-target-{1}";
    public const string JobMetadataPattern = "job-{0}-source-metadata";
    public const string JobDiagnosticPattern = "job-{0}-diagnostic-images";
    
    public static string GetSourceContainerName(string jobId) 
        => string.Format(JobSourcePattern, jobId);
    
    public static string GetTargetContainerName(string jobId, string languageCode) 
        => string.Format(JobTargetPattern, jobId, languageCode.ToLowerInvariant());
    
    public static string GetMetadataContainerName(string jobId) 
        => string.Format(JobMetadataPattern, jobId);
    
    public static string GetDiagnosticContainerName(string jobId) 
        => string.Format(JobDiagnosticPattern, jobId);
}

/// <summary>
/// Constants for file naming patterns
/// </summary>
public static class FileNamePatterns
{
    public const string ImagesPdfSuffix = "_images.pdf";
    public const string ImageMetadataJsonSuffix = "_image_metadata.json";
    
    public static string GetImagesPdfFileName(string originalFileName) 
        => $"{Path.GetFileNameWithoutExtension(originalFileName)}{ImagesPdfSuffix}";
    
    public static string GetImageMetadataFileName(string originalFileName) 
        => $"{Path.GetFileNameWithoutExtension(originalFileName)}{ImageMetadataJsonSuffix}";
}

/// <summary>
/// Constants for job phases
/// </summary>
public static class JobPhases
{
    public const string Initializing = "Initializing";
    public const string UploadingFiles = "Uploading Files";
    public const string ExtractingImages = "Extracting Images";
    public const string Starting = "Starting";
    public const string StartingTranslation = "Starting Translation";
    public const string TranslatingDocuments = "Translating Documents";
    public const string Translating = "Translating";
    public const string Processing = "Processing";
    public const string ReplacingImages = "Replacing Images";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string ValidationFailed = "Validation Failed";
    public const string NotFound = "Not Found";
    public const string Error = "Error";
}

/// <summary>
/// Constants for translation status
/// </summary>
public static class TranslationStatus
{
    public const string NotStarted = "NotStarted";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string ValidationFailed = "ValidationFailed";
    public const string NotFound = "NotFound";
    public const string Error = "Error";
    public const string InProgress = "InProgress";
    public const string Processing = "Processing";
}
