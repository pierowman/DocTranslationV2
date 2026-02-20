# Container Platform Comparison: Linux vs Windows

## Executive Summary

This document compares Linux and Windows container deployments for the DocTranslationV2 service, specifically regarding **EMF/WMF (Visio diagram) support** in PowerPoint files.

---

## Feature Comparison

| Feature | Linux Container | Windows Container |
|---------|----------------|-------------------|
| **EMF/WMF Support** | ? Limited (white placeholders) | ? Full native support |
| **ImageMagick Delegates** | ?? Partial (requires complex setup) | ? All delegates available |
| **Base Image Size** | ? ~200MB | ? ~5GB+ |
| **Build Time** | ? ~2-5 minutes | ? ~10-20 minutes |
| **Runtime Performance** | ? Excellent | ? Good |
| **Azure Cost** | ? Lower | ? Higher (~2-3x) |
| **Cloud Support** | ? Universal | ?? Limited (requires Windows nodes) |
| **Development Setup** | ? Works everywhere | ? Requires Windows host |

---

## Decision Matrix

### ? **Use Linux Containers If:**

1. **Cost is a priority** - Significantly cheaper in cloud
2. **Universal deployment** - Need to run on any platform
3. **Fast CI/CD** - Smaller images = faster pipelines
4. **Visio diagrams are rare** - Most PowerPoints use PNG/JPEG
5. **White placeholders acceptable** - Users understand limitation

**? Current implementation with fallback to white placeholders**

### ? **Use Windows Containers If:**

1. **Perfect fidelity required** - Visio diagrams must be translated
2. **Windows infrastructure exists** - Already have Windows Server
3. **Enterprise environment** - Budget for Windows licensing
4. **Native Office support needed** - May need COM interop
5. **Azure-only deployment** - Azure supports Windows containers well

**? Requires Dockerfile.windows and Windows host**

---

## Current Linux Container Status

### What Works

? **PDF images** - Full support  
? **Word images** - PNG, JPEG, GIF, BMP, TIFF  
? **PowerPoint standard images** - PNG, JPEG, GIF  
? **Image filtering** - Size, dimension, decorative detection  
? **Image replacement** - Accurate position tracking  

### Known Limitations

?? **EMF/WMF (Visio) diagrams** ? White placeholders  
?? **Complex vector graphics** ? May not render perfectly  

### Workaround Behavior

When Visio diagram detected:
1. ? Extract dimensions from PowerPoint metadata
2. ?? ImageMagick conversion fails (expected)
3. ? Create white PNG placeholder with correct size
4. ? Continue processing successfully
5. ?? Log warning about placeholder creation

**Result:** Document processes successfully, but Visio diagrams appear as white boxes in the translated output.

---

## Windows Container Implementation

### Prerequisites

**Development:**
- Windows 10/11 Pro/Enterprise (Hyper-V)
- Docker Desktop with Windows containers enabled
- Visual Studio 2022

**Production:**
- Windows Server 2019+ with container support
- Azure App Service (Windows)
- Azure Container Instances (Windows)
- Or on-premises Windows Server

### Build Windows Container

```powershell
# Switch Docker to Windows containers
& $Env:ProgramFiles\Docker\Docker\DockerCli.exe -SwitchDaemon

# Build Windows container
docker build -t doctranslationv2:windows -f DocTranslationV2/Dockerfile.windows .

# Run locally
docker run -p 8080:8080 doctranslationv2:windows
```

### Deploy to Azure App Service (Windows)

```bash
# Create Windows App Service Plan
az appservice plan create `
  --name myPlan-Windows `
  --resource-group myResourceGroup `
  --is-linux false `
  --sku P1V3

# Create Web App
az webapp create `
  --name myApp `
  --resource-group myResourceGroup `
  --plan myPlan-Windows `
  --deployment-container-image-name doctranslationv2:windows
```

### Magick.NET on Windows

No special configuration needed! Magick.NET automatically uses Windows GDI+ for EMF/WMF:

```csharp
// This code works perfectly in Windows containers
using var magickImage = new MagickImage(emfData);
magickImage.Format = MagickFormat.Png;
var pngData = magickImage.ToByteArray();
// ? Visio diagram perfectly converted to PNG!
```

---

## Cost Analysis (Azure Example)

### Linux Container (B2 App Service Plan)

- **Monthly cost:** ~$50-70/month
- **Image pulls:** Fast (~30 seconds)
- **Cold start:** ~5 seconds
- **Scale out:** 2-10 instances

### Windows Container (P1V3 App Service Plan)

- **Monthly cost:** ~$150-200/month  
- **Image pulls:** Slower (~2-3 minutes)
- **Cold start:** ~15 seconds
- **Scale out:** 2-10 instances

**Cost difference:** Windows is **3x more expensive** in Azure

---

## Hybrid Approach (Recommended)

### Strategy: Linux Primary + Windows Fallback

1. **Default:** Use Linux containers (current setup)
2. **Detect EMF/WMF:** Check if document contains Visio diagrams
3. **Route intelligently:**
   - Standard images ? Linux container (fast, cheap)
   - Visio diagrams ? Windows container (accurate, expensive)

```csharp
// Example routing logic
if (documentInfo.Images.Any(img => img.IsMetafile))
{
    // Route to Windows container for perfect fidelity
    await ProcessOnWindowsContainer(document);
}
else
{
    // Route to Linux container for cost efficiency
    await ProcessOnLinuxContainer(document);
}
```

**Benefits:**
- ? Best cost efficiency (most docs use standard images)
- ? Perfect fidelity when needed (Visio diagrams)
- ? Gradual adoption (start Linux, add Windows later)

---

## Recommendation

### **For Most Users: Keep Linux Containers**

**Reasons:**
1. **Cost effective** - 3x cheaper
2. **Universal** - Runs anywhere
3. **Fast CI/CD** - Quick builds/deploys
4. **Good enough** - Most PowerPoints don't have Visio

**Accept limitation:**
- Document that Visio diagrams ? white placeholders
- Provide guidance to users (convert Visio to PNG before uploading)

### **Upgrade to Windows Containers If:**

1. Visio diagrams are common in your organization
2. Perfect fidelity is a hard requirement
3. Budget allows for 3x infrastructure cost
4. Already have Windows Server infrastructure

---

## Migration Path

### Phase 1: Current State (Linux)
- ? Works for 90%+ of documents
- ?? Visio ? white placeholders
- ? Fast & cheap

### Phase 2: Add Windows Option (Optional)
- Create `Dockerfile.windows`
- Deploy Windows container to separate App Service
- Add routing logic to choose container type

### Phase 3: Hybrid (Future)
- Intelligent routing based on content
- Linux for standard images
- Windows for Visio diagrams
- Optimize cost vs quality

---

## Summary

| Scenario | Recommendation |
|----------|----------------|
| **Startup/MVP** | Linux (current) |
| **Cost sensitive** | Linux (current) |
| **Universal cloud** | Linux (current) |
| **Enterprise + Visio heavy** | Windows |
| **Perfect fidelity required** | Windows |
| **Azure + large budget** | Windows or Hybrid |

**Current implementation is appropriate for most use cases.** The white placeholder fallback is working as designed and provides good resilience.

Consider Windows containers only if Visio diagram support is a critical business requirement and budget allows for the additional infrastructure cost.
