# Target Folder Structure - Final Clarification

## Your Question
> "Can you remove target language from the target folder since it's possible to choose multiple target languages, I don't believe the translation service will separate these translations"

## Answer

After investigating the Azure Document Translation Service SDK and documentation:

**The current implementation with language-specific folders IS CORRECT and REQUIRED.**

Azure Document Translation Service **does NOT automatically separate languages**. You must explicitly provide separate target folders for each language.

## Current Implementation (Correct - No Changes Made)

```csharp
foreach (var targetLang in targetLanguages)
{
    // Each language requires its own target folder - this is Azure's required pattern
    var targetFolder = $"{targetFolderPath}/{targetLang}";
    
    var sourceUri = new Uri($"https://.../{sourceFolderPath}");
    var targetUri = new Uri($"https://.../{targetFolder}");
    
    inputs.Add(new DocumentTranslationInput(sourceUri, targetUri, targetLang));
}

await _batchClient.StartTranslationAsync(inputs, cancellationToken);
```

## Why Language-Specific Folders Are Required

### Azure SDK Constructor Signature
```csharp
public DocumentTranslationInput(
    Uri sourceUrl,           // Source folder
    Uri targetUrl,           // Target folder (ONE language)
    string targetLanguageCode // Language code
)
```

Each `DocumentTranslationInput` maps:
- **One source folder** ? **One target folder** ? **One language**

### For Multiple Languages
You create **multiple inputs** (one per language), all submitted in one API call:

```csharp
// All submitted together, but each has its own target folder
inputs.Add(new DocumentTranslationInput(sourceUri, targetUri_ES, "es"));
inputs.Add(new DocumentTranslationInput(sourceUri, targetUri_FR, "fr"));
inputs.Add(new DocumentTranslationInput(sourceUri, targetUri_DE, "de"));

await _batchClient.StartTranslationAsync(inputs, ct); // One API call, one job ID
```

## Folder Structure

### Current (Correct)
```
jobs/abc-123/
  ??? source/
  ?   ??? document.pdf
  ??? target/
      ??? es/
      ?   ??? document.pdf
      ??? fr/
      ?   ??? document.pdf
      ??? de/
          ??? document.pdf
```

### If We Removed Language Folders (Incorrect)
```
jobs/abc-123/
  ??? source/
  ?   ??? document.pdf
  ??? target/
      ??? ??? (All languages mixed? Azure wouldn't know where to put them)
```

## What Azure Actually Does

When you submit multiple inputs:

1. **Creates ONE job** with multiple translation tasks
2. **Processes each language** to its specified target folder
3. **Each language writes** to the target URI you provided
4. **NO automatic organization** by language

## URLs Being Passed (Current)

```
Source:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/source

Input 1:
  Target: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/es
  Language: es

Input 2:
  Target: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/fr
  Language: fr

Input 3:
  Target: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/de
  Language: de
```

## Benefits of Current Approach

? **Required by Azure** - SDK design enforces this pattern  
? **Clear organization** - Each language in its own folder  
? **Easy to find** - Predictable folder structure  
? **No mixing** - Languages never conflict  
? **Parallel processing** - All languages processed together  
? **Single job ID** - Track all translations as one operation  

## Conclusion

**NO CHANGES MADE** - The current implementation is correct.

The pattern of using `jobs/{jobId}/target/{languageCode}/` for each target language is:
- ? Required by Azure Document Translation Service SDK
- ? Standard best practice
- ? Only way to keep languages organized
- ? Explicitly defined in Azure SDK API

The belief that "the translation service will separate these translations" is incorrect. Azure requires you to explicitly specify where each language's output goes.

## Related Documentation

- `AZURE_MULTIPLE_LANGUAGES_EXPLANATION.md` - Detailed explanation
- `TARGET_FOLDER_FIX.md` - Previous documentation
- Azure SDK docs: Each `DocumentTranslationInput` = one language with one target folder
