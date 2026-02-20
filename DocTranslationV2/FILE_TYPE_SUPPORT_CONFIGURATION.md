# File Type Support Configuration - Batch vs Sync

## Overview
The Document Translation Service now properly distinguishes between file types supported by batch (async) translation and single document (sync) translation. Image processing is now correctly limited to async mode only and is disabled by default.

## Changes Made

### 1. Configuration (`appsettings.json`)

Added `SupportedFileTypes` configuration under `AzureTranslation`:

```json
{
  "AzureTranslation": {
    "SupportedFileTypes": {
      "Batch": [
        ".pdf", ".docx", ".pptx", ".xlsx",
        ".txt", ".html", ".htm", ".rtf",
        ".odt", ".ods", ".odp"
      ],
      "Sync": [
        ".pdf", ".docx", ".pptx",
        ".txt", ".html", ".htm"
      ],
      "ImageProcessingSupported": [
        ".pdf", ".docx"
      ]
    }
  }
}
```

**Rationale:**
- **Batch (Async)**: Supports more file types using Azure Document Translation batch API
- **Sync**: Fewer file types supported by Azure SingleDocumentTranslationClient
- **Image Processing**: Only available for PDF and Word documents in async mode

### 2. Configuration Model (`TranslationConfiguration.cs`)

Added new `SupportedFileTypes` class:

```csharp
public class SupportedFileTypes
{
    public List<string> Batch { get; set; } = new() { /* defaults */ };
    public List<string> Sync { get; set; } = new() { /* defaults */ };
    public List<string> ImageProcessingSupported { get; set; } = new() { ".pdf", ".docx" };
}
```

### 3. Service Layer (`DocumentTranslationService.cs`)

#### New Validation Methods

```csharp
public bool IsFileSupportedForMode(string fileName, bool isAsync)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    var supportedExtensions = isAsync 
        ? _settings.SupportedFileTypes.Batch 
        : _settings.SupportedFileTypes.Sync;
    return supportedExtensions.Contains(extension);
}

public bool SupportsImageProcessing(string fileName)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    return _settings.SupportedFileTypes.ImageProcessingSupported.Contains(extension);
}
```

#### Enhanced Validation in `TranslateDocumentsAsync`

```csharp
// Validate files based on processing mode
foreach (var file in request.Files)
{
    if (!IsFileSupportedForMode(file.FileName, request.UseAsyncProcessing))
    {
        var supportedExtensions = request.UseAsyncProcessing 
            ? string.Join(", ", _settings.SupportedFileTypes.Batch)
            : string.Join(", ", _settings.SupportedFileTypes.Sync);
        throw new InvalidOperationException(
            $"File '{file.FileName}' is not supported for {mode} translation. " +
            $"Supported formats: {supportedExtensions}");
    }
}

// Validate image processing is only requested for async mode
if (request.ProcessImages && !request.UseAsyncProcessing)
{
    _logger.LogWarning("Image processing requested for sync mode - disabling");
    request.ProcessImages = false;
}
```

### 4. Controller Layer (`TranslationController.cs`)

#### New Endpoint: GetSupportedFileTypes

```csharp
[HttpGet]
public IActionResult GetSupportedFileTypes()
{
    var config = HttpContext.RequestServices.GetRequiredService<IOptions<TranslationConfiguration>>();
    var fileTypes = config.Value.AzureTranslation.SupportedFileTypes;
    
    return Ok(new
    {
        batch = fileTypes.Batch,
        sync = fileTypes.Sync,
        imageProcessingSupported = fileTypes.ImageProcessingSupported
    });
}
```

#### Updated ViewModel Default

```csharp
public class TranslationRequestViewModel
{
    public bool ProcessImages { get; set; } = false; // Changed to false
}
```

### 5. UI Layer (`Index.cshtml`)

#### Image Processing Checkbox - Unchecked by Default

```html
<input class="form-check-input" type="checkbox" id="processImages" name="processImages">
<!-- Removed 'checked' attribute -->
```

#### Updated Help Text

```html
<div class="form-text" id="imageProcessingHelp">
    ? When enabled: Images extracted, translated, replaced<br>
    ? When disabled: Text translated, images unchanged<br>
    <span class="text-muted">Applies to: .docx and .pdf files (async mode only)</span>
</div>
```

#### New Warning for Sync Mode

```html
<div id="imageProcessingWarning" class="alert alert-warning mt-2" style="display: none;">
    <i class="bi bi-exclamation-triangle"></i> Image processing is only available in async mode
</div>
```

#### JavaScript Enhancements

```javascript
// Load supported file types from API
async function loadSupportedFileTypes() {
    const response = await fetch('/Translation/GetSupportedFileTypes');
    supportedFileTypes = await response.json();
}

// Validate file support for current mode
function isFileSupportedForMode(fileName, isAsync) {
    const extension = '.' + fileName.split('.').pop().toLowerCase();
    const supportedExtensions = isAsync ? supportedFileTypes.batch : supportedFileTypes.sync;
    return supportedExtensions.includes(extension);
}

// Update image processing availability
function updateImageProcessingAvailability() {
    const isAsync = document.getElementById('asyncMode').checked;
    const processImagesCheckbox = document.getElementById('processImages');
    
    if (!isAsync) {
        processImagesCheckbox.checked = false;
        processImagesCheckbox.disabled = true;
        imageProcessingWarning.style.display = 'block';
        return;
    }
    
    // Check if any files support image processing
    let hasImageSupportFiles = Array.from(files).some(f => supportsImageProcessing(f.name));
    processImagesCheckbox.disabled = !hasImageSupportFiles;
}
```

## Supported File Types Comparison

| File Type | Extension | Batch (Async) | Sync | Image Processing |
|-----------|-----------|---------------|------|------------------|
| PDF | .pdf | ? | ? | ? (async only) |
| Word | .docx | ? | ? | ? (async only) |
| PowerPoint | .pptx | ? | ? | ? |
| Excel | .xlsx | ? | ? | ? |
| Plain Text | .txt | ? | ? | ? |
| HTML | .html, .htm | ? | ? | ? |
| Rich Text | .rtf | ? | ? | ? |
| OpenDocument Text | .odt | ? | ? | ? |
| OpenDocument Sheet | .ods | ? | ? | ? |
| OpenDocument Presentation | .odp | ? | ? | ? |

## User Experience Flow

### Scenario 1: User Selects Async Mode with PDF

```
1. User uploads: document.pdf
2. Mode: Async selected
3. File list shows: document.pdf [1 MB] ???
4. Image Processing checkbox: ENABLED and AVAILABLE
5. User can check/uncheck based on needs
6. Submit enabled ?
```

### Scenario 2: User Selects Sync Mode with PDF

```
1. User uploads: document.pdf
2. Mode: Sync selected (or auto-selected for single file)
3. File list shows: document.pdf [1 MB] (no ??? icon)
4. Image Processing checkbox: DISABLED (grayed out)
5. Warning shows: "Image processing is only available in async mode"
6. Submit enabled ?
```

### Scenario 3: User Selects Sync Mode with XLSX

```
1. User uploads: data.xlsx
2. Mode: Sync selected
3. File list shows: data.xlsx [500 KB] ?? Not supported in sync mode
4. Image Processing checkbox: DISABLED
5. Submit button: DISABLED ?
6. Error: "Some files are not supported for sync mode"
```

### Scenario 4: User Switches from Sync to Async

```
1. User uploads: report.xlsx
2. Sync mode selected initially
3. File shows as unsupported
4. User switches to Async mode
5. File validation updates immediately
6. File now shows as supported ?
7. Submit button enabled
```

## Validation Logic

### Client-Side Validation (JavaScript)

```javascript
// Real-time validation when:
// - Files are selected
// - Processing mode changes
// - Target languages change

function validateForm() {
    const files = document.getElementById('fileInput').files;
    const isAsync = document.getElementById('asyncMode').checked;
    
    // Check each file
    Array.from(files).forEach(file => {
        if (!isFileSupportedForMode(file.name, isAsync)) {
            // Mark file as unsupported
            // Disable submit button
            // Show error message
        }
    });
}
```

### Server-Side Validation (C#)

```csharp
public async Task<TranslationResponse> TranslateDocumentsAsync(
    TranslationRequest request, ...)
{
    // Validate files based on processing mode
    foreach (var file in request.Files)
    {
        if (!IsFileSupportedForMode(file.FileName, request.UseAsyncProcessing))
        {
            throw new InvalidOperationException(
                $"File '{file.FileName}' is not supported for {mode} translation");
        }
    }
    
    // Force disable image processing for sync mode
    if (request.ProcessImages && !request.UseAsyncProcessing)
    {
        request.ProcessImages = false;
    }
}
```

## Configuration Updates Required

### Minimal Configuration (Uses Defaults)

No changes needed to `appsettings.json` if defaults are acceptable.

### Custom Configuration

```json
{
  "AzureTranslation": {
    "SupportedFileTypes": {
      "Batch": [".pdf", ".docx"],
      "Sync": [".pdf", ".txt"],
      "ImageProcessingSupported": [".pdf"]
    }
  }
}
```

## Testing Scenarios

### Test 1: Async Mode with Supported Files
- Upload: .pdf, .docx, .xlsx
- Mode: Async
- Expected: All files accepted, image processing available for PDF/DOCX

### Test 2: Sync Mode with Supported Files
- Upload: .pdf, .docx
- Mode: Sync
- Expected: Files accepted, image processing disabled

### Test 3: Sync Mode with Unsupported Files
- Upload: .xlsx, .odt
- Mode: Sync
- Expected: Files marked as unsupported, submit disabled

### Test 4: Mode Switch with Files Selected
- Upload: .xlsx
- Mode: Sync ? Async
- Expected: File changes from unsupported to supported

### Test 5: Image Processing in Sync Mode
- Upload: .docx
- Mode: Sync
- Check: Image Processing
- Expected: Checkbox automatically unchecked, disabled

## Breaking Changes

?? **Image Processing Default Changed**
- **Before**: `ProcessImages = true` (checked by default)
- **After**: `ProcessImages = false` (unchecked by default)

**Migration**: Users who want image processing enabled by default should update their code.

## Benefits

? **Accurate Validation**: Files validated against correct API capabilities
? **Better UX**: Clear feedback on what's supported in each mode
? **Error Prevention**: Server-side validation prevents invalid requests
? **Configuration-Driven**: Easy to update supported file types
? **Image Processing Control**: Users consciously opt-in to image processing
? **Mode-Aware UI**: UI adapts to selected processing mode

## API Reference

### Get Supported File Types

```http
GET /Translation/GetSupportedFileTypes
```

**Response:**
```json
{
  "batch": [".pdf", ".docx", ".pptx", ...],
  "sync": [".pdf", ".docx", ".pptx", ...],
  "imageProcessingSupported": [".pdf", ".docx"]
}
```

### Translate Documents (Updated Validation)

```http
POST /Translation/Translate
Content-Type: multipart/form-data
```

**Validation:**
- Files checked against mode-specific supported types
- Image processing forced to false for sync mode
- Clear error messages for unsupported files

## Summary

This implementation ensures:
1. ? Files are validated for the correct translation mode
2. ? Image processing is only available in async mode
3. ? Image processing is opt-in (unchecked by default)
4. ? Configuration-driven file type support
5. ? Real-time UI validation
6. ? Clear user feedback
7. ? Server-side validation as backup

The system now accurately reflects the capabilities of both Azure SDK clients and provides appropriate user guidance.
