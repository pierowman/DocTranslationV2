# Azure Document Translation - Multiple Target Languages

## Important Clarification

**After reviewing Azure SDK documentation and the original implementation:**

The current code pattern with **language-specific target folders IS CORRECT**.

## How Azure Translation Service Actually Works

Azure Document Translation Service does **NOT** automatically separate languages when you provide multiple target languages to a single target folder. Instead:

### The Correct Pattern (Current Implementation)

**Each target language requires its own `DocumentTranslationInput` with a separate target folder:**

```csharp
var inputs = new List<DocumentTranslationInput>();

foreach (var targetLang in targetLanguages)
{
    // Each language gets its own target folder - this is required by Azure
    var targetFolder = $"{targetFolderPath}/{targetLang}";
    
    var sourceUri = new Uri($"https://.../{sourceFolderPath}");
    var targetUri = new Uri($"https://.../{targetFolder}");
    
    // One input per language
    inputs.Add(new DocumentTranslationInput(sourceUri, targetUri, targetLang));
}

// Submit all inputs together in one API call
await _batchClient.StartTranslationAsync(inputs, cancellationToken);
```

## Why Language-Specific Folders Are Required

### The Azure SDK Constructor

```csharp
public DocumentTranslationInput(
    Uri sourceUrl,           // Source folder containing files to translate
    Uri targetUrl,           // Target folder - SPECIFIC TO ONE LANGUAGE
    string targetLanguageCode // The language for THIS translation
)
```

**Key Point**: Each `DocumentTranslationInput` represents one source?target?language mapping.

### Multiple Languages = Multiple Inputs

To translate to Spanish, French, and German:

```csharp
// Three separate inputs, all submitted together
inputs.Add(new DocumentTranslationInput(sourceUri, targetUri_ES, "es"));
inputs.Add(new DocumentTranslationInput(sourceUri, targetUri_FR, "fr"));
inputs.Add(new DocumentTranslationInput(sourceUri, targetUri_DE, "de"));

await _batchClient.StartTranslationAsync(inputs, cancellationToken);
```

## Folder Structure

### Source (One folder)
```
jobs/abc-123/source/
  ??? document.pdf
```

### Target (Separate folder per language)
```
jobs/abc-123/target/
  ??? es/
  ?   ??? document.pdf
  ??? fr/
  ?   ??? document.pdf
  ??? de/
      ??? document.pdf
```

## URLs Being Passed (Correct Implementation)

For translating to 3 languages:

```
Source:  https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/source

Inputs:
  1. Target: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/es, Lang: es
  2. Target: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/fr, Lang: fr
  3. Target: https://doctranslationstoragecbo.blob.core.windows.net/translations/jobs/abc-123/target/de, Lang: de
```

## What Happens

1. **One API call** submits all 3 inputs
2. **One job ID** is created for all translations
3. **Azure processes** all languages in parallel
4. **Each language** writes to its own target folder
5. **Job status** tracks all languages together

## Benefits

? **Clear organization** - Each language in its own folder  
? **Easy retrieval** - Know exactly where each translation is  
? **Parallel processing** - All languages processed together  
? **Single job tracking** - One job ID for all languages  
? **No conflicts** - Languages never mixed  

## Logging Output

```
[INFO] Translation input - Source: https://.../source, Target: https://.../target/es, Language: es
[INFO] Translation input - Source: https://.../source, Target: https://.../target/fr, Language: fr  
[INFO] Translation input - Source: https://.../source, Target: https://.../target/de, Language: de
[INFO] Starting batch translation with 3 input(s)
[INFO] Batch translation started with operation ID: <guid>
```

## Conclusion

**The original implementation is correct.** Azure Document Translation Service requires explicit target folders for each language. The pattern of using `target/{languageCode}/` for each language is:

- ? Required by the Azure SDK
- ? Best practice for organization
- ? Makes translations easy to find
- ? Prevents language mixing

**NO CHANGES NEEDED** to the current implementation.
