# Docker Container Configuration

## Current Setup: Windows Containers (Primary)

The project is configured to use **Windows containers** for **perfect Visio diagram (EMF/WMF) support**.

### Active Configuration

- **Dockerfile**: `Dockerfile` (Windows)
- **Target OS**: Windows
- **Image Base**: `mcr.microsoft.com/dotnet/aspnet:9.0-nanoserver-ltsc2022`
- **Perfect EMF/WMF Support**: ? Yes
- **Cost**: Higher (~$150-200/month in Azure)

---

## Switching Between Windows and Linux

### Option 1: Windows Containers (Current) ?

**Best for:** Perfect fidelity, Visio diagram support

**Requirements:**
- Windows 10/11 Pro/Enterprise with Hyper-V
- Docker Desktop in Windows container mode
- Windows Server 2019+ for production

**To use Windows containers:**

1. Switch Docker Desktop to Windows containers:
   ```powershell
   # Right-click Docker Desktop tray icon
   # Select "Switch to Windows containers..."
   ```

2. Ensure project settings:
   ```xml
   <!-- In DocTranslationV2.csproj -->
   <DockerDefaultTargetOS>Windows</DockerDefaultTargetOS>
   <DockerfileFile>Dockerfile.windows</DockerfileFile>
   ```

3. Build and run:
   ```powershell
   # Visual Studio: Press F5
   
   # Or manual build:
   docker build -t doctranslationv2:windows -f Dockerfile .
   docker run -p 8080:8080 doctranslationv2:windows
   ```

---

### Option 2: Linux Containers (Backup)

**Best for:** Cost optimization, universal cloud support

**Trade-offs:**
- ? 3x cheaper infrastructure
- ? Works on any cloud platform
- ? Faster builds (2-5 min vs 10-20 min)
- ?? Visio diagrams ? white placeholders

**To switch to Linux containers:**

1. Update `.csproj`:
   ```xml
   <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
   <!-- Remove: <DockerfileFile>Dockerfile.windows</DockerfileFile> -->
   ```

2. Copy Linux Dockerfile:
   ```powershell
   cd DocTranslationV2
   Copy-Item Dockerfile.linux Dockerfile -Force
   ```

3. Switch Docker Desktop to Linux containers:
   ```powershell
   # Right-click Docker Desktop tray icon
   # Select "Switch to Linux containers..."
   ```

4. Restart Visual Studio and press F5

---

## File Structure

```
DocTranslationV2/
??? Dockerfile              # Active Dockerfile (currently Windows)
??? Dockerfile.windows      # Windows container definition (perfect EMF/WMF)
??? Dockerfile.linux        # Linux container definition (white placeholder fallback)
??? DocTranslationV2.csproj # Points to active Dockerfile
??? DOCKER_CONFIGURATION.md # This file
```

---

## Deployment Targets

### Windows Container Deployment

**Azure App Service (Windows):**
```bash
# Create Windows App Service Plan
az appservice plan create \
  --name myPlan-Windows \
  --resource-group myResourceGroup \
  --is-linux false \
  --sku P1V3

# Create Web App
az webapp create \
  --name myApp \
  --resource-group myResourceGroup \
  --plan myPlan-Windows \
  --deployment-container-image-name <registry>/doctranslationv2:windows
```

**Azure Container Instances (Windows):**
```bash
az container create \
  --resource-group myResourceGroup \
  --name doctranslation-windows \
  --image <registry>/doctranslationv2:windows \
  --os-type Windows \
  --cpu 2 \
  --memory 4
```

---

### Linux Container Deployment

**Azure App Service (Linux):**
```bash
# Create Linux App Service Plan
az appservice plan create \
  --name myPlan-Linux \
  --resource-group myResourceGroup \
  --is-linux true \
  --sku B2

# Create Web App
az webapp create \
  --name myApp \
  --resource-group myResourceGroup \
  --plan myPlan-Linux \
  --deployment-container-image-name <registry>/doctranslationv2:linux
```

**Azure Container Instances (Linux):**
```bash
az container create \
  --resource-group myResourceGroup \
  --name doctranslation-linux \
  --image <registry>/doctranslationv2:linux \
  --os-type Linux \
  --cpu 2 \
  --memory 2
```

---

## Build Commands

### Windows Container

```powershell
# Ensure Docker is in Windows mode
docker version

# Build
docker build -t doctranslationv2:windows -f Dockerfile .

# Run locally
docker run -p 8080:8080 --name doctranslation doctranslationv2:windows

# View logs
docker logs -f doctranslation

# Stop
docker stop doctranslation
```

### Linux Container

```bash
# Ensure Docker is in Linux mode
docker version

# Build
docker build -t doctranslationv2:linux -f Dockerfile.linux .

# Run locally
docker run -p 8080:8080 --name doctranslation doctranslationv2:linux

# View logs
docker logs -f doctranslation

# Stop
docker stop doctranslation
```

---

## Expected Behavior Differences

### Windows Container Output (Perfect Fidelity)

```
? Detected metafile format image/x-emf
? Extracted EMF/WMF dimensions: 921x688
? Successfully converted image/x-emf to PNG using native GDI+
? Visio diagram perfectly rendered
```

### Linux Container Output (White Placeholder)

```
? Detected metafile format image/x-emf
? Extracted EMF/WMF dimensions: 921x688
?? ImageMagick conversion failed (expected - no Windows GDI+)
? Created white placeholder PNG (3974 bytes)
? Successfully converted image/x-emf to PNG [fallback]
```

---

## Troubleshooting

### Issue: "image operating system mismatch"

**Symptom:**
```
Error: image operating system "windows" cannot be used on this platform
```

**Solution:**
Switch Docker Desktop to Windows containers mode.

---

### Issue: "no matching manifest for linux/amd64"

**Symptom:**
```
Error: no matching manifest for linux/amd64 in manifest list
```

**Solution:**
You're trying to run a Windows image on Linux. Switch Docker to Windows mode or use `Dockerfile.linux`.

---

### Issue: Visual Studio can't find Dockerfile

**Solution:**
Ensure `.csproj` has correct settings:
```xml
<DockerDefaultTargetOS>Windows</DockerDefaultTargetOS>
<DockerfileFile>Dockerfile.windows</DockerfileFile>
```

---

## Performance Comparison

| Metric | Windows | Linux |
|--------|---------|-------|
| **Image Size** | ~5GB | ~200MB |
| **Build Time** | 10-20 min | 2-5 min |
| **Cold Start** | ~15 sec | ~5 sec |
| **EMF/WMF Support** | ? Perfect | ?? Placeholder |
| **Azure Cost/Month** | ~$150-200 | ~$50-70 |
| **Cloud Support** | Limited | Universal |

---

## Recommendations

### Current Configuration (Windows) ?

**Keep Windows containers if:**
- ? Perfect Visio diagram fidelity is required
- ? Cost is not a primary concern
- ? You have Windows Server infrastructure
- ? Most documents contain Visio diagrams

**This is your current setup and provides the best quality.**

### Switch to Linux If:

- [ ] Cost becomes a priority (3x savings)
- [ ] Visio diagrams are rare (<10% of documents)
- [ ] Users can convert Visio ? PNG before upload
- [ ] Need universal cloud platform support

---

## Related Documentation

- [Container Platform Comparison](./CONTAINER_PLATFORM_COMPARISON.md) - Detailed analysis
- [Quick Decision Guide](./QUICK_DECISION_GUIDE.md) - 30-second decision tree
- [PowerPoint EMF/WMF Fix](./POWERPOINT_EMF_WMF_FIX.md) - Technical details
- [Docker Build Guide](./DOCKER_BUILD.md) - Build instructions

---

## Current Status

? **Windows Containers Active**
- Perfect Visio diagram support
- Native Windows GDI+ rendering
- Production-ready for Windows hosting

?? **Linux Containers Available as Backup**
- Cost-optimized alternative
- White placeholder fallback for Visio
- Ready to switch if needed
