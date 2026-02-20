# Translation Status Quick Reference

## Status Flow

```
NotStarted ? Running ? Succeeded
                ?
           Cancelled
                ?
              Failed
                ?
        ValidationFailed
```

## Phase Descriptions

| Azure Status      | Current Phase      | What It Means                                    | User Action                |
|-------------------|-------------------|--------------------------------------------------|----------------------------|
| `NotStarted`      | Initializing      | Job created, waiting to start                    | Wait                       |
| `Running` (early) | Starting          | Preparing documents, no active translation yet   | Wait                       |
| `Running` (mid)   | Translating       | Documents actively being translated              | Wait, watch progress       |
| `Running` (late)  | Processing        | Finalizing translation                           | Wait                       |
| `Succeeded`       | Completed         | All documents translated successfully            | Download files             |
| `Failed`          | Failed            | Some/all documents failed to translate           | Check error, retry         |
| `Cancelled`       | Cancelled         | User or system cancelled the job                 | Start new job if needed    |
| `ValidationFailed`| Validation Failed | Configuration or permissions issue               | Fix permissions, retry     |
| N/A               | Not Found         | Job doesn't exist                                | Check Job ID               |
| N/A               | Error             | Error checking status                            | Retry, check connectivity  |

## Status Icons

| Icon | Meaning           |
|------|-------------------|
| ??   | Initializing      |
| ??   | Starting          |
| ??   | Translating       |
| ??   | Processing        |
| ?   | Completed         |
| ?   | Failed            |
| ??   | Cancelled         |
| ??   | Validation Failed |
| ??   | Time info         |

## Document States

| State           | Icon | Description                                    |
|-----------------|------|------------------------------------------------|
| Succeeded       | ?    | Document translated successfully               |
| InProgress      | ??   | Document currently being translated            |
| NotStarted      | ?   | Document waiting to be translated              |
| Failed          | ?    | Document failed to translate                   |
| Cancelled       | ??   | Document translation was cancelled             |

## Progress Bar Colors

| Color         | Status                          |
|---------------|---------------------------------|
| Blue animated | Translation in progress         |
| Green         | Translation succeeded           |
| Red           | Translation failed              |

## Badge Colors

| Color   | Status                                |
|---------|---------------------------------------|
| Primary | NotStarted, Running                   |
| Success | Succeeded, Completed                  |
| Danger  | Failed, ValidationFailed              |
| Warning | Cancelled                             |
| Dark    | Other/Unknown                         |

## Typical Timeline

### Small Job (1-2 files, single language)
```
00:00 - Initializing
00:05 - Starting
00:10 - Translating (50%)
00:15 - Translating (100%)
00:20 - Completed
```

### Medium Job (5-10 files, 2-3 languages)
```
00:00 - Initializing
00:10 - Starting
00:30 - Translating (25%)
01:00 - Translating (50%)
01:30 - Translating (75%)
02:00 - Translating (100%)
02:10 - Completed
```

### Large Job (20+ files, multiple languages)
```
00:00 - Initializing
00:20 - Starting
01:00 - Translating (10%)
03:00 - Translating (50%)
05:00 - Translating (90%)
06:00 - Completed
```

## Common Scenarios

### ? Happy Path
```
?? Initializing ? ?? Starting ? ?? Translating ? ? Completed
```
**Action**: Download your files

### ? Validation Failure
```
?? Initializing ? ?? Validation Failed
```
**Cause**: Missing permissions or incorrect configuration
**Action**: 
1. Check Azure Translation Service has "Storage Blob Data Contributor" role
2. Wait 5-10 minutes for permission propagation
3. Verify storage account and container names are correct

### ?? User Cancellation
```
?? Translating (50%) ? ?? Cancelled
```
**Cause**: User clicked Cancel or timeout
**Action**: Start a new translation if needed

### ?? Partial Failure
```
?? Translating (100%) ? ? Failed (8/10 succeeded)
```
**Cause**: Some documents couldn't be translated (corrupted, protected, etc.)
**Action**: 
1. Download successful translations
2. Check error details for failed files
3. Retry failed files separately

## Status Check Frequency

| Phase              | Check Interval |
|--------------------|----------------|
| Active Translation | Every 5 seconds|
| Completed          | Cached 30 min  |
| Failed             | Cached 30 min  |
| Cancelled          | Cached 30 min  |

## Error Troubleshooting

### "Job not found"
- Job ID is incorrect
- Job was deleted
- Job never existed

### "Validation failed"
- Missing Storage Blob Data Contributor role
- Firewall blocking Translation Service
- Incorrect storage URIs
- Container doesn't exist

### "Translation failed"
- Document corrupted or invalid
- Document too large (>40 MB)
- Unsupported language pair
- Password-protected document
- Complex formatting issues

## API Response Structure

```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Running",
  "currentPhase": "Translating",
  "detailedStatus": "?? Translating documents... (5/10 completed)\n   • In Progress: 2\n   • Pending: 3\n?? Elapsed time: 2 minute(s) 15 second(s)",
  "totalDocuments": 10,
  "translatedDocuments": 5,
  "failedDocuments": 0,
  "documentsInProgress": 2,
  "documentsNotStarted": 3,
  "percentComplete": 50,
  "createdOn": "2024-01-15T14:30:45Z",
  "lastModified": "2024-01-15T14:33:00Z",
  "elapsedTime": "00:02:15",
  "targetLanguages": ["es", "fr", "de"],
  "errorMessage": ""
}
```

## Best Practices

### For Users
1. **Don't refresh the page** - Status updates automatically
2. **Wait for completion** - Translation takes time depending on file size
3. **Check detailed status** - Error messages provide troubleshooting steps
4. **Download promptly** - Files may be deleted after cleanup

### For Administrators
1. **Monitor long-running jobs** - Jobs >10 minutes may indicate issues
2. **Check Azure quotas** - Translation service has rate limits
3. **Verify permissions** - Ensure managed identity setup is correct
4. **Review failed jobs** - Pattern of failures may indicate configuration issues

## Quick Reference Card

```
?????????????????????????????????????????????????
?         TRANSLATION STATUS REFERENCE          ?
?????????????????????????????????????????????????
? ?? Initializing      ? Wait                   ?
? ?? Starting          ? Wait                   ?
? ?? Translating       ? Watch progress         ?
? ?? Processing        ? Almost done            ?
? ? Completed         ? Download files         ?
? ? Failed            ? Check errors           ?
? ?? Cancelled         ? Restart if needed      ?
? ?? Validation Failed ? Fix permissions        ?
?????????????????????????????????????????????????
? Updates: Every 5 seconds while active         ?
? Cache: 30 minutes for completed jobs          ?
?????????????????????????????????????????????????
? Need Help? Check error message or docs       ?
?????????????????????????????????????????????????
```
