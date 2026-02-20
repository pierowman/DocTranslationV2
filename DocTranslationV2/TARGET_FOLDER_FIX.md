# Target Folder - Final Implementation

## Current Implementation (Correct)

### For Batch Translation (Multiple Languages)

**Single target folder for all languages:**

```csharp
private async Task<string> StartBatchTranslationAsync(...)
{
    // Use single target folder - Azure separates languages automatically
    var sourceUri = new Uri($"https://{accountName}.blob.core.windows.net/{container}/{sourceFolderPath}");
    var targetUri = new Uri($"https://{accountName}.blob.core.windows.net/{container}/{targetFolderPath}");
    
    // Create targets for all languages pointing to same folder
    var targets = targetLanguages.Select(lang => new DocumentTranslationInputTarget(targetUri, lang)).ToList();
    
    var input = new DocumentTranslationInput(sourceUri, targets);
    
    await _batchClient.StartTranslationAsync(new[] { input }, cancellationToken);
}
```

## URLs Being Passed

For your configuration:
- Storage: `doctranslationstoragecbo`
- Container: `translations`

### Batch Translation (Multiple Languages)

**Single request with multiple targets:**

```
Source:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/source
Target:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target
  ??? Target Languages: [es, fr, de]
```

**NOT** (previous incorrect approach):
```
? Target: https://.../jobs/abc-123/target/es
? Target: https://.../jobs/abc-123/target/fr
? Target: https://.../jobs/abc-123/target/de
```

### Sync Translation (Single Language at a Time)

Sync translation still uses language-specific folders:

```
Source:  (in-memory stream)
Target:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/es
```

## How Azure Handles This

### Batch Translation
- You provide: **One target folder + multiple languages**
- Azure creates: **Files organized by language** (Azure's internal logic)
- Result: All translations in target folder, separated by language

### Expected Output

Azure will organize files like:
```
jobs/abc-123/target/
  ??? document_es.pdf
  ??? document_fr.pdf
  ??? document_de.pdf
```

Or possibly:
```
jobs/abc-123/target/
  ??? es/
  ?   ??? document.pdf
  ??? fr/
  ?   ??? document.pdf
  ??? de/
      ??? document.pdf
```

The exact structure is determined by Azure Translation Service.

## Why This is Correct

1. **Azure's Design**: The service is built to handle multiple languages to a single target
2. **More Efficient**: One API call instead of multiple calls
3. **Automatic Organization**: Azure handles language separation
4. **Simpler Code**: No manual folder path construction per language

## What Changed

### Before
```csharp
// ? Created separate target folders per language
foreach (var targetLang in targetLanguages)
{
    var targetFolder = $"{targetFolderPath}/{targetLang}";
    var targetUri = new Uri($"https://.../{targetFolder}");
    inputs.Add(new DocumentTranslationInput(sourceUri, targetUri, targetLang));
}
```

### Now
```csharp
// ? Single target folder for all languages
var targetUri = new Uri($"https://.../{targetFolderPath}");
var targets = targetLanguages.Select(lang => 
    new DocumentTranslationInputTarget(targetUri, lang)).ToList();
var input = new DocumentTranslationInput(sourceUri, targets);
```

## Expected Behavior

1. **Upload source files** ? `jobs/abc-123/source/document.pdf`
2. **Pass single target URI** with multiple language targets
3. **Translation starts**
4. **Azure writes translated files** organized by language
5. **Cleanup** removes entire job folder

## Key Points

? **No language code in target folder path** (for batch)  
? **Single target URI** for all languages  
? **Azure handles organization** automatically  
? **More efficient** - one API call  
? **Follows Azure patterns** correctly  

## Sync vs Batch

| Mode | Target Path | Reason |
|------|-------------|--------|
| **Batch** | `jobs/{jobId}/target` | Azure handles multi-language |
| **Sync** | `jobs/{jobId}/target/{lang}` | We control per-call output |

## Logging

New log format:
```
[INFO] Translation input - Source: https://.../source, Target: https://.../target, Languages: es, fr, de
[INFO] Starting batch translation with 3 target language(s)
[INFO] Batch translation started with operation ID: <guid>
```

## Related Documentation

- `TARGET_FOLDER_STRUCTURE_FIX.md` - Detailed change explanation
- `MANAGED_IDENTITY_SETUP.md` - Permissions configuration
- `API_KEY_AUTHENTICATION.md` - Authentication setup
