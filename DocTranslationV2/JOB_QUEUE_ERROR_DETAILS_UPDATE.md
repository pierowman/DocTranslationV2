# Job Queue Error Details Enhancement

## Summary
Updated the Translation Job Queue to display detailed error information for ValidationFailed and Failed jobs, matching the comprehensive error diagnostics available in the logging system.

## Problem
Jobs with `ValidationFailed` status were not showing the "View Details" button in the job queue because:
- `GetAllTranslationJobsAsync` only populated `ErrorMessage` when `DocumentsFailed > 0`
- For validation failures, documents never start, so `DocumentsFailed` is 0
- This left the error column showing "-" instead of actionable error information

## Solution

### 1. Updated `GetAllTranslationJobsAsync` Method
**File**: `DocTranslationV2\Services\DocumentTranslationService.cs`

Modified the method to populate detailed error messages for all failure scenarios:

```csharp
var statusString = statusResponse.Status.ToString();

// Populate error messages for failed states
if (statusString == "ValidationFailed" || statusString == "Failed" || statusResponse.DocumentsFailed > 0)
{
    // Try to get detailed error information
    try
    {
        var errorDetails = await GetDocumentErrorDetailsAsync(statusResponse.Id, cancellationToken);
        if (!string.IsNullOrEmpty(errorDetails))
        {
            jobInfo.ErrorMessage = errorDetails;
        }
        else if (statusString == "ValidationFailed")
        {
            // Provide detailed validation failure message if no document-level errors available
            jobInfo.ErrorMessage = BuildValidationFailedMessage(statusResponse);
        }
        else if (statusResponse.DocumentsFailed > 0)
        {
            jobInfo.ErrorMessage = BuildDocumentFailedMessage(statusResponse);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Could not retrieve detailed error for job {JobId}", statusResponse.Id);
        // Fallback to simple error message
        if (statusString == "ValidationFailed")
        {
            jobInfo.ErrorMessage = "Validation failed: Translation Service cannot access blob storage. Check managed identity permissions.";
        }
        else if (statusResponse.DocumentsFailed > 0)
        {
            jobInfo.ErrorMessage = $"{statusResponse.DocumentsFailed} document(s) failed to translate";
        }
    }
}
else if (statusString == "Cancelled")
{
    jobInfo.ErrorMessage = "Translation job was cancelled";
}
```

### 2. Enhanced Job Queue UI (Already Updated)
**File**: `DocTranslationV2\Views\Translation\Jobs.cshtml`

The job queue UI already has:
- Modal dialog for displaying full error details
- "View Details" button that appears when `errorMessage` exists
- Copy functionality for error details
- Proper formatting for multi-line error messages

## What This Fixes

### Before
- Jobs with `ValidationFailed` status showed "-" in the Error column
- No way to see why validation failed without checking logs
- Users had to manually investigate permission issues

### After
- Jobs with `ValidationFailed` status show a "View Details" button
- Clicking the button displays comprehensive error information including:
  - Permission issues with specific Azure commands to fix
  - Storage account firewall configuration
  - URI validation errors
  - Container existence checks
  - File accessibility issues
- Error details can be copied for troubleshooting

## Error Information Displayed

### For ValidationFailed Status
The error modal now shows:
```
Validation Failed

Total Documents: X
Documents Not Started: X
Failed Documents: 0

Common causes of validation failure:

1. PERMISSION ISSUES (Most Common)
   The Azure Translation Service cannot access your blob storage.
   Required: 'Storage Blob Data Contributor' role on the storage account.

   To fix:
   - Go to Azure Portal ? Your Storage Account ? Access Control (IAM)
   - Click '+ Add' ? 'Add role assignment'
   - Select 'Storage Blob Data Contributor' role
   - Assign to your Translation Service's managed identity
   - Wait 5-10 minutes for permission propagation

2. STORAGE ACCOUNT FIREWALL
   If your storage account has firewall rules:
   - Add the Translation Service's subnet to allowed networks
   - Or enable 'Allow Azure services on the trusted services list'

3. INCORRECT URIs
   Verify the source and target blob URIs are correct:
   - Storage Account: {AccountName}
   - Container: {ContainerName}
   - Check for typos in account name or container name

4. CONTAINER DOES NOT EXIST
   Ensure the container exists in the storage account

5. FILES NOT ACCESSIBLE
   Verify the source files exist at the specified location

Job ID: {JobId}
Created: {Timestamp}
```

### For Failed Status with Document Errors
Shows document-level errors when available:
```
Document validation failed: /jobs/{jobId}/source/filename.pdf
  Error Code: InvalidDocumentAccessLevel
  Message: The Translation Service cannot access the document
```

### For Failed Status (General)
Shows detailed troubleshooting guide for common translation failure scenarios.

## Benefits

1. **Self-Service Troubleshooting**: Users can identify and fix permission issues without developer intervention
2. **Consistent Information**: Job queue shows the same detailed diagnostics available in logs
3. **Better UX**: No need to dig through Application Insights or server logs
4. **Actionable Guidance**: Error messages include specific steps to resolve issues
5. **Copy Functionality**: Users can copy error details to share with support

## Testing

To verify the enhancement works:

1. **Create a job with validation failure**:
   - Remove the 'Storage Blob Data Contributor' role from Translation Service
   - Submit a translation job
   - Go to Jobs page

2. **Verify error details appear**:
   - Job should show status "Validation Failed"
   - Error column should show "View Details" button
   - Click button to see comprehensive error message

3. **Verify copy functionality**:
   - Click "Copy Error Details" button
   - Paste into notepad to verify full message copied

4. **Test with other error states**:
   - Failed documents (wrong file format)
   - Cancelled jobs
   - Verify appropriate messages appear

## Related Files

- `DocTranslationV2\Services\DocumentTranslationService.cs` - Updated `GetAllTranslationJobsAsync`
- `DocTranslationV2\Views\Translation\Jobs.cshtml` - Error details modal UI
- `DocTranslationV2\DETAILED_ERROR_DIAGNOSTICS.md` - Original error diagnostics implementation

## Notes

- Error retrieval uses the same `GetDocumentErrorDetailsAsync` method used by `GetTranslationStatusAsync`
- Detailed errors are only available for jobs in the active operations cache
- For older jobs without cached operations, fallback messages with troubleshooting steps are provided
- Error messages include timestamps, job IDs, and configuration details for debugging
