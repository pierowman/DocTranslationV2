# Target Folder Structure Change

## Issue Identified

Previously, the code was creating separate target folders for each language:
```
jobs/{jobId}/target/es/
jobs/{jobId}/target/fr/
jobs/{jobId}/target/de/
```

This was incorrect because Azure Document Translation Service **automatically handles language separation** when you provide multiple target languages.

## Correct Implementation

Now using a **single target folder** for all languages:
```
jobs/{jobId}/target/
```

Azure Translation Service will automatically organize the translated files by language within this folder.

## Code Changes

### Before (Incorrect)
```csharp
foreach (var targetLang in targetLanguages)
{
    var targetFolder = $"{targetFolderPath}/{targetLang}";  // Adding language to path
    var targetUri = new Uri($"https://.../{targetFolder}");
    inputs.Add(new DocumentTranslationInput(sourceUri, targetUri, targetLang));
}
```

### After (Correct)
```csharp
// Use single target folder for all languages
var sourceUri = new Uri($"https://.../{sourceFolderPath}");
var targetUri = new Uri($"https://.../{targetFolderPath}");  // No language in path

// Create targets for all languages pointing to same target folder
var targets = targetLanguages.Select(lang => new DocumentTranslationInputTarget(targetUri, lang)).ToList();

var input = new DocumentTranslationInput(sourceUri, targets);
```

## How Azure Handles Multiple Languages

When you submit a translation job with multiple target languages to a single target folder:

1. **Azure automatically creates language-specific subdirectories** or **prefixes**
2. **Files are organized** by Azure's internal logic
3. **You don't need to pre-create** separate folders per language

### Expected Output Structure

Azure will organize files like:
```
jobs/abc-123/target/
  ??? [Azure organizes by language automatically]
      ??? document_es.pdf
      ??? document_fr.pdf
      ??? document_de.pdf
```

OR (depending on Azure's implementation):
```
jobs/abc-123/target/
  ??? es/document.pdf
  ??? fr/document.pdf
  ??? de/document.pdf
```

The exact organization is handled by Azure Translation Service.

## Benefits

? **Simpler code** - Single target URI instead of loop  
? **More efficient** - One API call per source folder, not per language  
? **Correct pattern** - Follows Azure's recommended approach  
? **Less error-prone** - No manual folder path construction  

## Updated URLs

### Previous (Incorrect)
```
Source:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/source
Target:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/es
Target:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/fr
Target:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/de
```

### Current (Correct)
```
Source:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/source
Target:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target
  ??? With targets: [es, fr, de]
```

## Impact on Sync Translation

Note: Sync (single document) translation still uses language-specific folders because it's a different API:

```csharp
// Sync translation - still uses language-specific path
var targetFolderPath = $"jobs/{jobId}/target/{targetLang}";
```

This is correct because:
- Sync API translates one file at a time
- Each language requires a separate API call
- We control the output path per call

## Testing

To verify this works:

1. Submit a translation job with **multiple target languages** (e.g., Spanish, French, German)
2. Check blob storage structure after translation completes
3. Verify all translated files are in `jobs/{jobId}/target/`
4. Verify files are organized by language (however Azure organizes them)

## Logging

New log output:
```
[INFO] Translation input - Source: https://.../source, Target: https://.../target, Languages: es, fr, de
[INFO] Starting batch translation with 3 target language(s)
[INFO] Batch translation started with operation ID: <guid>
```

## Related Files

- `DocumentTranslationService.cs` - Updated `StartBatchTranslationAsync()` method
- This change only affects **batch (async) translations**, not sync translations
