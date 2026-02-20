# Testing Guide

This guide provides comprehensive testing scenarios for the Document Translation Application.

## Pre-Testing Checklist

- [ ] Azure resources are provisioned and configured
- [ ] Application configuration is complete
- [ ] All NuGet packages are restored
- [ ] Application builds successfully
- [ ] Application Insights is receiving telemetry

## Unit Testing Preparation

### Test Files Required

Create a `TestFiles` folder with the following sample documents:

1. **Simple Text File** (`test.txt`)
   - Small plain text file (< 1KB)
   - Contains text in English

2. **PDF Document** (`document.pdf`)
   - PDF with text content
   - Size: 100KB - 1MB

3. **Word Document** (`document.docx`)
   - DOCX with text and images
   - Size: 100KB - 5MB

4. **PDF with Images** (`image_document.pdf`)
   - PDF containing embedded images
   - Multiple pages with images

5. **Large Document** (`large.pdf`)
   - File size > 50MB
   - For testing async processing

6. **Multiple Files**
   - Prepare 5-10 small files for bulk testing

## Test Scenarios

### 1. Basic Translation Tests

#### Test 1.1: Single File Translation (Sync)
**Objective:** Verify synchronous translation of a single file

**Steps:**
1. Navigate to `/Translation`
2. Upload `test.txt` (small file)
3. Select "Auto-detect language" or choose "English"
4. Select target language: "Spanish"
5. Choose "Sync Processing"
6. Click "Start Translation"

**Expected Results:**
- Progress bar shows translation progress
- Status updates in real-time
- Translated file appears in results
- Download button is enabled
- File downloads successfully

**Validation:**
- Open downloaded file
- Verify content is translated
- Verify file format is preserved

#### Test 1.2: Single File Translation (Async)
**Objective:** Verify asynchronous translation

**Steps:**
1. Upload `document.pdf`
2. Select target language: "French"
3. Choose "Async Processing"
4. Start translation

**Expected Results:**
- Job ID is generated
- Status polling begins
- Progress updates every 5 seconds
- Translation completes within expected time
- Files available for download

#### Test 1.3: Multi-language Translation
**Objective:** Translate to multiple target languages simultaneously

**Steps:**
1. Upload `document.docx`
2. Select target languages: Spanish, French, German
3. Choose "Async Processing"
4. Start translation

**Expected Results:**
- Three separate translated files are created
- Each file is in the correct language
- All files are available for download
- Results are organized by language

### 2. Bulk Translation Tests

#### Test 2.1: Multiple Files Upload
**Objective:** Verify bulk file handling

**Steps:**
1. Select 5-10 different files
2. Verify file list displays all files
3. Select target languages
4. System automatically selects "Async Processing"
5. Start translation

**Expected Results:**
- All files are uploaded successfully
- Processing handles all files
- Results show all translated files
- Each file can be downloaded individually

#### Test 2.2: Large File Translation
**Objective:** Test handling of files > 50MB

**Steps:**
1. Upload large file (> 50MB)
2. Select target language
3. Use async processing
4. Monitor progress

**Expected Results:**
- Upload completes successfully
- Translation doesn't timeout
- Status updates continue for > 5 minutes
- Large file translates successfully

### 3. Image Handling Tests

#### Test 3.1: PDF with Images
**Objective:** Verify image extraction and translation

**Steps:**
1. Upload PDF containing images
2. Select target language
3. Start translation
4. Monitor logs for image extraction messages

**Expected Results:**
- System detects images in PDF
- Images are extracted
- Separate images PDF is created
- Both text and images are translated
- Final document contains translated images

#### Test 3.2: Word Document with Images
**Objective:** Test image handling in DOCX files

**Steps:**
1. Upload DOCX with embedded images
2. Translate to target language
3. Download result

**Expected Results:**
- Images are extracted from Word document
- Text is translated
- Images are processed separately
- Final document maintains formatting
- Images are re-integrated correctly

### 4. Language Detection Tests

#### Test 4.1: Auto Language Detection
**Objective:** Verify automatic language detection

**Steps:**
1. Upload file in unknown language (e.g., Spanish)
2. Check "Auto-detect language"
3. Select target language: English
4. Translate

**Expected Results:**
- System correctly identifies source language
- Translation proceeds without errors
- Result is accurate

#### Test 4.2: Manual Language Selection
**Objective:** Test explicit language selection

**Steps:**
1. Upload English document
2. Uncheck "Auto-detect"
3. Manually select "English" as source
4. Translate to Spanish

**Expected Results:**
- Manual selection is respected
- Translation completes successfully

### 5. Error Handling Tests

#### Test 5.1: Unsupported File Type
**Objective:** Verify validation of file types

**Steps:**
1. Try uploading `.exe` or `.mp3` file
2. Submit form

**Expected Results:**
- Error message: "File type not supported"
- Upload is rejected
- Supported formats are displayed

#### Test 5.2: No Target Language Selected
**Objective:** Verify validation

**Steps:**
1. Upload valid file
2. Don't select any target language
3. Try to submit

**Expected Results:**
- Error message displayed
- Form doesn't submit
- User is prompted to select language

#### Test 5.3: File Size Limit
**Objective:** Test file size validation

**Steps:**
1. Try uploading file > 500MB
2. Submit form

**Expected Results:**
- Error message about file size limit
- Upload is rejected
- Maximum size is displayed

#### Test 5.4: Network Interruption
**Objective:** Test resilience to network issues

**Steps:**
1. Start a large file translation
2. Simulate network disconnection
3. Reconnect
4. Check status

**Expected Results:**
- Application handles disconnection gracefully
- Status polling resumes after reconnection
- Translation continues on server side

### 6. Cleanup Tests

#### Test 6.1: Manual Cleanup
**Objective:** Verify cleanup functionality

**Steps:**
1. Complete a translation job
2. Download files
3. Click "Delete Temporary Files"
4. Confirm deletion

**Expected Results:**
- Confirmation dialog appears
- Files are deleted from blob storage
- Success message is displayed
- Source and target folders are removed

#### Test 6.2: Verify Cleanup in Azure
**Objective:** Confirm files are actually deleted

**Steps:**
1. Open Azure Portal
2. Navigate to Storage Account
3. Check `jobs/{jobId}` folder
4. Verify folder is deleted

**Expected Results:**
- Folders are completely removed
- No orphaned files remain

### 7. Performance Tests

#### Test 7.1: Concurrent Users
**Objective:** Test multiple simultaneous translations

**Steps:**
1. Open application in 3-5 different browsers
2. Start translations simultaneously
3. Monitor performance

**Expected Results:**
- All translations process successfully
- No conflicts between jobs
- Each user sees their own results
- Response times are acceptable

#### Test 7.2: Long-running Translation
**Objective:** Test translations > 5 minutes

**Steps:**
1. Upload very large file or many files
2. Start async translation
3. Monitor for > 10 minutes

**Expected Results:**
- Status polling continues
- No timeout errors
- Translation completes eventually
- Results are retrievable

### 8. Logging and Monitoring Tests

#### Test 8.1: Application Insights Telemetry
**Objective:** Verify logging to App Insights

**Steps:**
1. Perform several translations
2. Open Azure Portal ? Application Insights
3. Check telemetry data

**Expected Results:**
- Request traces are logged
- Custom events are recorded
- Errors are captured
- Performance metrics are available

#### Test 8.2: Error Logging
**Objective:** Verify error logging

**Steps:**
1. Cause an error (invalid config, etc.)
2. Check Application Insights logs
3. Review error details

**Expected Results:**
- Errors are logged with full details
- Stack traces are captured
- Context information is included
- Errors are categorized correctly

### 9. Security Tests

#### Test 9.1: Authentication
**Objective:** Verify Azure AD authentication

**Steps:**
1. Clear credentials
2. Restart application
3. Attempt translation

**Expected Results:**
- Application authenticates via EntraID
- No manual credential entry required
- Authentication succeeds

#### Test 9.2: SAS Token Expiration
**Objective:** Test handling of expired tokens

**Steps:**
1. Start translation
2. Wait for extended period
3. Try to download files

**Expected Results:**
- Application handles token renewal
- Downloads succeed
- Or appropriate error message if token expired

### 10. UI/UX Tests

#### Test 10.1: File Selection UI
**Objective:** Verify UI responsiveness

**Steps:**
1. Select multiple files
2. Verify file list updates
3. Check file sizes are displayed
4. Verify sync/async mode changes

**Expected Results:**
- File list displays immediately
- File sizes are formatted correctly
- Mode switches based on file count
- UI is responsive and intuitive

#### Test 10.2: Progress Indicators
**Objective:** Test progress feedback

**Steps:**
1. Start translation
2. Monitor progress section
3. Verify updates

**Expected Results:**
- Progress bar updates smoothly
- Status text is clear and informative
- Job details are displayed
- Time estimates are reasonable

#### Test 10.3: Responsive Design
**Objective:** Test on different devices

**Steps:**
1. Open on desktop browser
2. Open on tablet
3. Open on mobile device

**Expected Results:**
- Layout adapts to screen size
- All functions are accessible
- Buttons and controls are usable
- File upload works on all devices

## Automated Testing

### Sample Unit Test (C#)

```csharp
using Xunit;
using DocTranslationV2.Services;

public class FileValidationTests
{
    [Theory]
    [InlineData("test.pdf", true)]
    [InlineData("test.docx", true)]
    [InlineData("test.exe", false)]
    [InlineData("test.mp3", false)]
    public void IsFileSupported_ReturnsExpectedResult(string fileName, bool expected)
    {
        var result = FileValidationHelper.IsExtensionSupported(
            Path.GetExtension(fileName));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ValidateFile_LargeFile_ReturnsError()
    {
        var (isValid, error) = FileValidationHelper.ValidateFile(
            "large.pdf", 
            600000000, // 600MB
            false);
        
        Assert.False(isValid);
        Assert.Contains("exceeds maximum", error);
    }
}
```

## Testing Checklist

### Before Release
- [ ] All test scenarios pass
- [ ] No console errors
- [ ] Application Insights receives data
- [ ] File cleanup works correctly
- [ ] Error messages are user-friendly
- [ ] Performance is acceptable
- [ ] Security tests pass
- [ ] Documentation is complete

### Regression Testing
- [ ] Re-run all tests after code changes
- [ ] Test with different Azure configurations
- [ ] Verify backwards compatibility
- [ ] Test with various file types
- [ ] Verify edge cases

## Known Issues / Limitations

Document any known issues or limitations discovered during testing:

1. **PDF Image Replacement**: Current implementation is simplified
2. **Very Large Files**: May require additional memory optimization
3. **Concurrent Jobs**: Maximum of X concurrent jobs per instance

## Test Results Template

| Test ID | Test Name | Date | Result | Notes |
|---------|-----------|------|--------|-------|
| 1.1 | Single Sync | 2024-01-15 | ? Pass | |
| 1.2 | Single Async | 2024-01-15 | ? Pass | |
| ... | ... | ... | ... | |

## Reporting Issues

When reporting issues, include:
1. Test scenario ID
2. Steps to reproduce
3. Expected vs actual results
4. Screenshots/logs
5. Environment details
6. Application Insights correlation ID
