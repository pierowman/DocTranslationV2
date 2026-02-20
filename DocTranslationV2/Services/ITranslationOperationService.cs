using Azure.AI.Translation.Document;

namespace DocTranslationV2.Services;

/// <summary>
/// Handles direct interactions with Azure Translation Service API
/// </summary>
public interface ITranslationOperationService
{
    /// <summary>
    /// Starts a batch translation operation
    /// </summary>
    Task<string> StartBatchTranslationAsync(
        string sourceContainerUri,
        string targetContainerUri,
        string targetLanguage,
        string? sourceLanguage,
        bool autoDetect,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Starts synchronous translation for a single document
    /// </summary>
    Task<Stream> TranslateSingleDocumentAsync(
        Stream documentStream,
        string fileName,
        string targetLanguage,
        string? sourceLanguage,
        bool autoDetect,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the status of a translation operation
    /// </summary>
    Task<TranslationStatusResult> GetOperationStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all translation operations (for listing jobs)
    /// </summary>
    IAsyncEnumerable<TranslationStatusResult> GetAllOperationsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets document-level status for an operation
    /// </summary>
    IAsyncEnumerable<DocumentStatus> GetDocumentStatusesAsync(
        string operationId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cancels a translation operation
    /// </summary>
    Task<bool> CancelOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Waits for a translation operation to complete
    /// </summary>
    Task<DocumentTranslationStatus> WaitForCompletionAsync(
        string operationId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets or caches a translation operation for monitoring
    /// </summary>
    DocumentTranslationOperation? GetCachedOperation(string operationId);
    
    /// <summary>
    /// Caches a translation operation for future status checks
    /// </summary>
    void CacheOperation(string operationId, DocumentTranslationOperation operation);
}

/// <summary>
/// Represents the status of a translation operation
/// </summary>
public class TranslationStatusResult
{
    public string Id { get; set; } = string.Empty;
    public DocumentTranslationStatus Status { get; set; }
    public int DocumentsTotal { get; set; }
    public int DocumentsSucceeded { get; set; }
    public int DocumentsFailed { get; set; }
    public int DocumentsInProgress { get; set; }
    public int DocumentsNotStarted { get; set; }
    public int DocumentsCanceled { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset LastModified { get; set; }
}

/// <summary>
/// Represents document-level status
/// </summary>
public class DocumentStatus
{
    public Uri? SourceDocumentUri { get; set; }
    public Uri? TranslatedDocumentUri { get; set; }
    public DocumentTranslationStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
