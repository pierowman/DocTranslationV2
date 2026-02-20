# Download Not Working - Diagnostic Guide

## Problem
After translation completes successfully, clicking the download button doesn't work.

## Possible Causes

### 1. **Container Name Mismatch** (Most Likely)
The translated files are in container `job-{guid}-target`, but the download might be looking in the wrong place.

### 2. **File Not Actually Translated**
Azure Translation Service completed but didn't create the output file.

### 3. **JavaScript Error**
The download button click isn't triggering the download request.

### 4. **Server Error**
The server receives the request but can't find the file.

---

## Diagnostic Steps

### Step 1: Check Browser Console
1. Open browser Developer Tools (F12)
2. Click on "Console" tab
3. Click a Download button
4. Look for errors:

**What to look for:**
```javascript
// Error fetching
Error: Download failed

// Network error
Failed to fetch

// 404 Not Found
POST /Translation/DownloadFile 404

// 500 Server Error
POST /Translation/DownloadFile 500
```

**If you see an error:** Note the exact message and move to Step 3.

**If no error:** The request might not be happening at all - check Step 2.

---

### Step 2: Check Network Tab
1. Open Developer Tools (F12)
2. Click "Network" tab
3. Click a Download button
4. Look for a POST request to `/Translation/DownloadFile`

**What you should see:**
```
Method: POST
URL: https://localhost:.../Translation/DownloadFile
Status: 200 OK
Type: application/octet-stream
```

**If you see Status 404 or 500:**
- Click on the request
- Click "Preview" or "Response" tab
- You should see error details like:
  ```json
  {
    "error": "File not found: job-abc-123-target/test.pdf"
  }
  ```

**If you don't see any POST request:**
- The JavaScript might have an error
- Check Console tab for JavaScript errors
- Verify `currentJobId` is set

---

### Step 3: Check Application Logs
Look in your application output/logs for messages like:

**Successful download should show:**
```
Attempting to download file from blob storage at path: job-abc-123-target/test.pdf
Container-based download - Container: job-abc-123-target, File: test.pdf
Successfully downloaded 12345 bytes from job-abc-123-target/test.pdf
```

**If container doesn't exist:**
```
ERROR: Container job-abc-123-target does not exist
```
**FIX:** The translation didn't complete successfully or the container was deleted.

**If file doesn't exist:**
```
ERROR: Blob test.pdf does not exist in container job-abc-123-target
Listing all blobs in container job-abc-123-target:
  Found blob: document.pdf
  Found blob: report.docx
```
**FIX:** The filename doesn't match. Check if the file has a different name.

**If no logs at all:**
- The request isn't reaching the server
- Check Network tab (Step 2)
- Check JavaScript errors (Step 1)

---

### Step 4: Verify Container in Azure Portal
1. Go to Azure Portal
2. Navigate to your Storage Account
3. Click "Containers" under "Data storage"
4. Look for containers named `job-{guid}-target`

**What you should see:**
```
? job-abc-123-def-456-source
? job-abc-123-def-456-target
? translations (default container)
```

**Click on the target container:**
- You should see your translated files
- File names should match the original file names

**If container is missing:**
- Translation didn't complete successfully
- Container might have been cleaned up
- Check translation logs

**If container exists but empty:**
- Translation failed silently
- Check Azure Translation Service portal for job status
- Look for validation errors in logs

---

## Common Issues & Fixes

### Issue 1: "Container not found"

**Symptom:**
```
ERROR: Container job-abc-123-target does not exist
```

**Cause:** The translation job didn't create the container or it was deleted.

**Fix:**
1. Check if translation actually completed (look for "Operation completed with status: Succeeded" in logs)
2. Verify managed identity permissions are correct
3. Try a new translation

---

### Issue 2: "File not found in container"

**Symptom:**
```
ERROR: Blob test.pdf does not exist in container job-abc-123-target
Listing all blobs in container:
  Found blob: other-file.pdf
```

**Cause:** Filename mismatch - the file has a different name than expected.

**Fix:**
Check the `TranslatedBlobUrl` in the response:
```json
{
  "translatedFiles": [
    {
      "originalFileName": "test.pdf",
      "targetLanguage": "es",
      "translatedBlobUrl": "job-abc-123-target/test.pdf"  ? Should match actual filename
    }
  ]
}
```

If the URL is correct but file doesn't exist, the translation didn't produce output.

---

### Issue 3: JavaScript `currentJobId` is null

**Symptom:** Download button doesn't do anything, no network request.

**Console shows:**
```javascript
currentJobId: null
```

**Fix:**
The `currentJobId` variable isn't being set. Check the form submission handler:

```javascript
// Should be setting currentJobId after translation
const result = await response.json();
currentJobId = result.jobId;  ? This line must execute
```

---

### Issue 4: CORS Error

**Symptom:**
```
Access to fetch at 'https://...' from origin '...' has been blocked by CORS policy
```

**Fix:** This shouldn't happen with same-origin requests. If you see this:
1. Verify you're accessing the app via the correct URL
2. Check if you have a proxy or reverse proxy configuration
3. Make sure the request is going to the same host/port

---

### Issue 5: Response is not a file

**Symptom:** Browser shows JSON error instead of downloading file.

**Example response:**
```json
{
  "error": "File not found"
}
```

**Fix:** The server is returning an error. Check:
1. Application logs for the specific error
2. The blob path being requested
3. If the container and file exist in Azure

---

## Quick Test

To quickly test if downloads work, try this in browser console:

```javascript
// Test download with a known blob path
fetch('/Translation/DownloadFile', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ 
        blobPath: 'job-YOUR-JOB-ID-target/YOUR-FILE-NAME.pdf',
        applyImageReplacement: false,
        jobId: 'YOUR-JOB-ID'
    })
})
.then(response => {
    console.log('Status:', response.status);
    if (!response.ok) {
        return response.json().then(err => {
            console.error('Error:', err);
        });
    }
    return response.blob();
})
.then(blob => {
    if (blob) {
        console.log('Downloaded blob size:', blob.size);
        // Try to download
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'test-download.pdf';
        a.click();
    }
})
.catch(error => console.error('Fetch error:', error));
```

Replace:
- `YOUR-JOB-ID` with your actual job ID
- `YOUR-FILE-NAME.pdf` with your actual filename

**Expected result:** File should download.

**If it fails:** Check the console error message.

---

## Enable Detailed Logging

The code now has detailed logging. To see all logs:

### In Development (Visual Studio)
1. Run the application
2. Look in the "Output" window
3. Select "DocTranslationV2 - ASP.NET Core Web Server"
4. You'll see all the download attempts and errors

### In Production (App Service)
1. Go to Azure Portal ? Your App Service
2. Click "Log stream" under "Monitoring"
3. You'll see real-time logs including download attempts

---

## What the Logs Tell You

### Successful Download:
```
INFO: Attempting to download file from blob storage at path: job-abc-123-target/test.pdf
INFO: Container-based download - Container: job-abc-123-target, File: test.pdf
INFO: Successfully downloaded 12345 bytes from job-abc-123-target/test.pdf
```
? **Everything is working!**

### Container Not Found:
```
INFO: Attempting to download file from blob storage at path: job-abc-123-target/test.pdf
INFO: Container-based download - Container: job-abc-123-target, File: test.pdf
ERROR: Container job-abc-123-target does not exist
```
? **Problem:** Translation didn't create the container.
**Action:** Check translation completion status.

### File Not Found:
```
INFO: Attempting to download file from blob storage at path: job-abc-123-target/test.pdf
INFO: Container-based download - Container: job-abc-123-target, File: test.pdf
ERROR: Blob test.pdf does not exist in container job-abc-123-target
INFO: Listing all blobs in container job-abc-123-target:
INFO:   Found blob: other-file.pdf
```
? **Problem:** Filename mismatch or translation didn't produce this file.
**Action:** Check what files actually exist in the container.

---

## Next Steps

1. ? Run a new translation
2. ? Wait for completion
3. ? Open browser Developer Tools (F12)
4. ? Click Download button
5. ? Check Console tab for errors
6. ? Check Network tab for the POST request
7. ? Check Application logs for detailed messages
8. ? Report back with:
   - Browser console errors (if any)
   - Network request status code
   - Application log messages
   - What you see in Azure Portal containers

With this information, we can pinpoint exactly what's wrong!
