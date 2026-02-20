using System.Collections.Concurrent;
using Azure;
using Azure.AI.Translation.Document;
using Microsoft.Extensions.Options;
using DocTranslationV2.Models;

namespace DocTranslationV2.Services;

/// <summary>
/// Handles direct interactions with Azure Translation Service API
/// </summary>
public class TranslationOperationService : ITranslationOperationService
{
    private readonly DocumentTranslationClient _batchClient;
    private readonly SingleDocumentTranslationClient _singleDocClient;
    private readonly ConcurrentDictionary<string, DocumentTranslationOperation> _cachedOperations = new();
    private readonly ILogger<TranslationOperationService> _logger;
    private readonly AzureTranslationSettings _settings;

    public TranslationOperationService(
        IOptions<TranslationConfiguration> config,
        ICredentialService credentialService,
        ILogger<TranslationOperationService> logger)
    {
        _settings = config.Value.AzureTranslation;
        _logger = logger;

        var credential = credentialService.GetTranslationServiceCredential();
        _batchClient = new DocumentTranslationClient(new Uri(_settings.Endpoint), credential);
        _singleDocClient = new SingleDocumentTranslationClient(new Uri(_settings.Endpoint), credential);
    }

    public async Task<string> StartBatchTranslationAsync(
        string sourceContainerUri,
        string targetContainerUri,
        string targetLanguage,
        string? sourceLanguage,
        bool autoDetect,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting batch translation - Source: {Source}, Target: {Target}, Language: {Language}",
                sourceContainerUri, targetContainerUri, targetLanguage);

            var sourceUri = new Uri(sourceContainerUri);
            var targetUri = new Uri(targetContainerUri);

            // Give Azure Storage time to commit files
            await Task.Delay(2000, cancellationToken);

            DocumentTranslationInput input;

            if (!autoDetect && !string.IsNullOrEmpty(sourceLanguage))
            {
                _logger.LogInformation("Using source language: {SourceLang}", sourceLanguage);
                var translationSource = new TranslationSource(sourceUri) { LanguageCode = sourceLanguage };
                var translationTarget = new TranslationTarget(targetUri, targetLanguage);
                input = new DocumentTranslationInput(translationSource, new[] { translationTarget });
            }
            else
            {
                _logger.LogInformation("Using auto-detect for source language");
                input = new DocumentTranslationInput(sourceUri, targetUri, targetLanguage);
            }

            var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);

            if (operation == null || string.IsNullOrEmpty(operation.Id))
            {
                throw new InvalidOperationException("Translation operation created but no operation ID returned");
            }

            // Cache the operation
            _cachedOperations[operation.Id] = operation;

            _logger.LogInformation("Batch translation started with operation ID: {OperationId}", operation.Id);
            return operation.Id;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure RequestFailedException: Status={Status}, ErrorCode={ErrorCode}",
                ex.Status, ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting batch translation");
            throw;
        }
    }

    public async Task<Stream> TranslateSingleDocumentAsync(
        Stream documentStream,
        string fileName,
        string targetLanguage,
        string? sourceLanguage,
        bool autoDetect,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Translating single document {FileName} to {TargetLanguage}",
                fileName, targetLanguage);

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = GetContentType(extension);

            var fileData = new MultipartFormFileData(fileName, documentStream, contentType);
            var documentContent = new DocumentTranslateContent(fileData);

            var translationResult = await _singleDocClient.TranslateAsync(
                targetLanguage,
                documentContent,
                sourceLanguage: autoDetect ? null : sourceLanguage,
                cancellationToken: cancellationToken);

            return translationResult.Value.ToStream();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure translation failed for {FileName}: Status={Status}, ErrorCode={ErrorCode}",
                fileName, ex.Status, ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in synchronous translation for {FileName}", fileName);
            throw;
        }
    }

    public async Task<TranslationStatusResult> GetOperationStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        await foreach (var status in _batchClient.GetTranslationStatusesAsync(cancellationToken: cancellationToken))
        {
            if (status.Id == operationId)
            {
                return new TranslationStatusResult
                {
                    Id = status.Id,
                    Status = status.Status,
                    DocumentsTotal = status.DocumentsTotal,
                    DocumentsSucceeded = status.DocumentsSucceeded,
                    DocumentsFailed = status.DocumentsFailed,
                    DocumentsInProgress = status.DocumentsInProgress,
                    DocumentsNotStarted = status.DocumentsNotStarted,
                    DocumentsCanceled = status.DocumentsCanceled,
                    CreatedOn = status.CreatedOn,
                    LastModified = status.LastModified
                };
            }
        }

        throw new InvalidOperationException($"Operation {operationId} not found");
    }

    public async IAsyncEnumerable<TranslationStatusResult> GetAllOperationsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var status in _batchClient.GetTranslationStatusesAsync(cancellationToken: cancellationToken))
        {
            yield return new TranslationStatusResult
            {
                Id = status.Id,
                Status = status.Status,
                DocumentsTotal = status.DocumentsTotal,
                DocumentsSucceeded = status.DocumentsSucceeded,
                DocumentsFailed = status.DocumentsFailed,
                DocumentsInProgress = status.DocumentsInProgress,
                DocumentsNotStarted = status.DocumentsNotStarted,
                DocumentsCanceled = status.DocumentsCanceled,
                CreatedOn = status.CreatedOn,
                LastModified = status.LastModified
            };
        }
    }

    public async IAsyncEnumerable<DocumentStatus> GetDocumentStatusesAsync(
        string operationId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_cachedOperations.TryGetValue(operationId, out var operation))
        {
            _logger.LogWarning("Operation {OperationId} not found in cache", operationId);
            yield break;
        }

        await foreach (var document in operation.GetDocumentStatusesAsync())
        {
            yield return new DocumentStatus
            {
                SourceDocumentUri = document.SourceDocumentUri,
                TranslatedDocumentUri = document.TranslatedDocumentUri,
                Status = document.Status,
                ErrorCode = document.Error?.Code,
                ErrorMessage = document.Error?.Message
            };
        }
    }

    public async Task<bool> CancelOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Canceling operation {OperationId}", operationId);

            DocumentTranslationOperation? operation;

            if (!_cachedOperations.TryGetValue(operationId, out operation))
            {
                operation = new DocumentTranslationOperation(operationId, _batchClient);
            }

            await operation.UpdateStatusAsync(cancellationToken);

            if (!operation.HasCompleted)
            {
                await operation.CancelAsync(cancellationToken);
                _cachedOperations.TryRemove(operationId, out _);

                _logger.LogInformation("Operation {OperationId} canceled successfully", operationId);
                return true;
            }
            else
            {
                _logger.LogWarning("Cannot cancel operation {OperationId} - already completed with status {Status}",
                    operationId, operation.Status);
                return false;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Operation {OperationId} not found for cancellation", operationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling operation {OperationId}", operationId);
            throw;
        }
    }

    public async Task<DocumentTranslationStatus> WaitForCompletionAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        if (!_cachedOperations.TryGetValue(operationId, out var operation))
        {
            throw new InvalidOperationException($"Operation {operationId} not found in cache");
        }

        await operation.WaitForCompletionAsync(cancellationToken);
        return operation.Status;
    }

    public DocumentTranslationOperation? GetCachedOperation(string operationId)
    {
        return _cachedOperations.TryGetValue(operationId, out var operation) ? operation : null;
    }

    public void CacheOperation(string operationId, DocumentTranslationOperation operation)
    {
        _cachedOperations[operationId] = operation;
        _logger.LogDebug("Cached operation {OperationId}", operationId);
    }

    private string GetContentType(string extension)
    {
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".rtf" => "application/rtf",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp" => "application/vnd.oasis.opendocument.presentation",
            _ => "application/octet-stream"
        };
    }
}
