# Detailed Error Diagnostics Implementation

## Summary
Enhanced the Azure Translation Service error handling to retrieve and display detailed error information when jobs fail validation or encounter errors during processing.

## Changes Made

### DocumentTranslationService.cs

#### 1. Enhanced GetTranslationStatusAsync Method
- Modified to retrieve detailed document-level errors for failed and validation-failed jobs
- Now calls new `GetDocumentErrorDetailsAsync` method when errors are detected
- Provides specific error codes and messages from Azure Translation Service

#### 2. New GetDocumentErrorDetailsAsync Method
This new private method retrieves detailed error information for each document in a failed job:

```csharp
private async Task<string> GetDocumentErrorDetailsAsync(string jobId, CancellationToken cancellationToken)
```

**Features:**
- Iterates through all documents in the translation operation
- Captures errors for documents with `Failed` or `ValidationFailed` status
- Extracts:
  - Document source URI
  - Error code
  - Error message
  - Additional details when available
- Logs each error for debugging
- Returns formatted error messages for display to users

## Error Information Retrieved

When a translation job fails, the system now retrieves and displays:

1. **Document Identifier**: The source document URI or name
2. **Error Code**: Azure-specific error code (e.g., `InvalidRequest`, `Unauthorized`, `InternalServerError`)
3. **Error Message**: Detailed message explaining what went wrong
4. **Per-Document Errors**: Individual errors for each document in batch translations

## Common Error Scenarios

### ValidationFailed Errors
When you see a `ValidationFailed` status, the detailed errors will show specific reasons such as:
- **Error Code: Unauthorized** - Translation service cannot access blob storage (missing permissions)
- **Error Code: InvalidDocumentAccessLevel** - Document access configuration issues
- **Error Code: InvalidRequest** - Malformed URIs or invalid request parameters

### Document Processing Errors
For documents that fail during processing:
- **Error Code: DocumentFormatInvalid** - File format issues
- **Error Code: UnsupportedLanguagePair** - Translation not supported for language combination
- **Error Code: DocumentSizeLimit** - Document exceeds size limits

## Benefits

1. **Faster Debugging**: Immediately see the root cause without checking Azure Portal
2. **Multiple Document Support**: See errors for all failed documents in a batch
3. **Better User Experience**: Show users specific actionable error messages
4. **Comprehensive Logging**: All errors are logged for troubleshooting

## Testing

To test the enhanced error handling:

1. **Test Permission Issues**:
   - Remove Storage Blob Data Contributor role from Translation Service
   - Submit a translation job
   - Check that detailed permission errors are displayed

2. **Test Invalid Documents**:
   - Submit an unsupported file format
   - Check that format-specific errors are shown

3. **Test Invalid URIs**:
   - Modify blob storage configuration incorrectly
   - Check that URI/access errors are displayed

## Example Error Output

Before:
```
Status: ValidationFailed
Error: Validation failed: Azure Translation Service cannot access the blob storage...
```

After:
```
Status: ValidationFailed
Error: Document: /jobs/abc123/source/document.pdf
  Error Code: Unauthorized
  Message: The Translation Service does not have permission to access the blob storage container.

Document: /jobs/abc123/source/report.docx
  Error Code: Unauthorized
  Message: The Translation Service does not have permission to access the blob storage container.
```

## Notes

- Error details are retrieved only when jobs have failed or validation-failed status
- The method handles cases where error information is not available
- Errors are logged for both user display and system diagnostics
- No additional Azure API calls are made for successful jobs (performance optimization)
