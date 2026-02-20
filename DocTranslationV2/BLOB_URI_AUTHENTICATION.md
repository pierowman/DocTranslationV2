# Blob URI Authentication Issue - Common Cause of Validation Failures

## The Problem

When Azure Translation Service tries to access your blob storage, it needs **authentication**. There are two ways:

### Option 1: Managed Identity (What you're trying to use)
```
https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc/source
```
- Translation Service uses its **Managed Identity** to authenticate
- Requires: Translation Service has "Storage Blob Data Contributor" role

### Option 2: SAS Tokens (What older versions often use)
```
https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc/source?sv=2021-08-06&ss=b&srt=sco&sp=rl&se=2024-12-31T23:59:59Z&st=2024-01-01T00:00:00Z&spr=https&sig=SIGNATURE
```
- URIs include `?` query string with SAS token
- No role assignment needed - authentication is in the URL

## Check Your Older Version

Look at your older working version's logs. Do the URIs have a `?` in them?

**If older version uses SAS tokens:**
```
Source: https://.../source?sv=2021-08-06&ss=b&srt=sco&sp=rl&...
```

**If new version does NOT use SAS tokens:**
```
Source: https://.../source
```

Then this is your issue!

## Quick Fix - Add SAS Token Generation

If your older version uses SAS tokens, here's how to add them to the new version:

### 1. Check if BlobStorageService Generates SAS Tokens

Look in `BlobStorageService.cs` - do you have a method like this?

```csharp
public Uri GenerateSasUri(string blobPath, TimeSpan expirationTime)
{
    // Generates URI with SAS token
}
```

### 2. If Not, Add SAS Token Generation

Add this to your `IBlobStorageService` interface:

```csharp
public interface IBlobStorageService
{
    // ...existing methods...
    
    /// <summary>
    /// Generate a SAS token URI for a folder path (for Translation Service access)
    /// </summary>
    Uri GenerateContainerSasUri(string folderPath, TimeSpan expirationTime);
}
```

And implement it in `BlobStorageService.cs`:

```csharp
public Uri GenerateContainerSasUri(string folderPath, TimeSpan expirationTime)
{
    var containerClient = _blobServiceClient.GetBlobContainerClient(_settings.ContainerName);
    
    // Generate SAS token with read and list permissions
    var sasBuilder = new BlobSasBuilder
    {
        BlobContainerName = _settings.ContainerName,
        Resource = "c", // Container level
        StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
        ExpiresOn = DateTimeOffset.UtcNow.Add(expirationTime),
        Protocol = SasProtocol.Https
    };
    
    // Translation Service needs Read and List permissions
    sasBuilder.SetPermissions(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List);
    
    // Generate the SAS token
    var sasToken = containerClient.GenerateSasUri(sasBuilder);
    
    // Construct URI with folder path and SAS token
    var baseUri = containerClient.Uri;
    var folderUri = new Uri($"{baseUri}/{folderPath}?{sasToken.Query.TrimStart('?')}");
    
    return folderUri;
}
```

### 3. Update StartBatchTranslationAsync to Use SAS URIs

Change this section:

```csharp
// OLD - Plain URIs (relies on managed identity)
var sourceUri = new Uri($"https://{_blobSettings.AccountName}.blob.core.windows.net/{_blobSettings.ContainerName}/{sourceFolderPath}");
var targetUri = new Uri($"https://{_blobSettings.AccountName}.blob.core.windows.net/{_blobSettings.ContainerName}/{targetFolder}");
```

To this:

```csharp
// NEW - SAS URIs (includes authentication in URL)
var sasExpiration = TimeSpan.FromHours(24); // Token valid for 24 hours
var sourceUri = _blobStorageService.GenerateContainerSasUri(sourceFolderPath, sasExpiration);
var targetUri = _blobStorageService.GenerateContainerSasUri(targetFolder, sasExpiration);
```

## Why This Might Be Your Issue

1. **Older version**: May have used SAS tokens (authentication in URL)
2. **New version**: Trying to use Managed Identity (requires role assignments)
3. **Managed Identity**: May not be properly configured or propagated

## Verify Your Current Setup

### Check Current URIs in Logs

Look for this log message:
```
Translation input - Source: {SourceUri}, Target: {TargetUri}
```

**Does the URI have a `?` with query parameters?**
- **No `?`**: You're trying to use Managed Identity (need proper roles)
- **Has `?`**: You're using SAS tokens (should work if token is valid)

### Check Old Version URIs

Compare your old version's logs:
- If old version has `?` in URIs ? **Add SAS token generation**
- If old version has no `?` ? **Check managed identity setup**

## Decision Tree

```
Is your older version using SAS tokens (URIs have ? in them)?
?
?? YES ? Add SAS token generation to new version
?         (Use code above)
?
?? NO ? Both versions use Managed Identity
         ?
         ?? Check these:
            1. Translation Service has system-assigned identity enabled
            2. Identity has "Storage Blob Data Contributor" role
            3. Role assignment has propagated (wait 10 min after adding)
            4. Storage firewall allows Translation Service
```

## Testing SAS Tokens

If you add SAS tokens, the URIs will look like:

```
https://doctranslationstoragecbo.blob.core.windows.net/doctranslation/jobs/abc-123/source?sv=2021-08-06&ss=b&srt=co&sp=rl&se=2024-12-31T23:59:59Z&st=2024-01-01T00:00:00Z&spr=https&sig=LONG_SIGNATURE_STRING
```

**Benefits of SAS tokens:**
- ? No managed identity setup needed
- ? Works immediately
- ? Fine-grained control over permissions
- ? Time-limited access (more secure)

**Downsides:**
- ?? Tokens expire (need to generate fresh ones)
- ?? Slightly more complex code

## Next Step

**Check your older version's logs or code** - specifically look at what the `sourceUri` and `targetUri` look like when passed to the Translation Service.

If they have SAS tokens (query string with `?`), that's your answer! Add SAS token generation to this version.
