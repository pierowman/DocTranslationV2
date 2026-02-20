# Windows Container Setup Guide

## Quick Start: Running with Windows Containers

### Prerequisites ?

**Development Machine:**
- [x] Windows 10/11 Pro, Enterprise, or Education
- [x] Hyper-V enabled
- [x] Docker Desktop for Windows installed
- [x] Visual Studio 2022 (17.8 or later)

**Verify Docker Installation:**
```powershell
docker --version
# Docker version 24.0.0 or later
```

---

## Step 1: Switch Docker to Windows Containers

### Option A: Docker Desktop GUI
1. Right-click **Docker Desktop** icon in system tray
2. Click **"Switch to Windows containers..."**
3. Wait for Docker to restart (~30 seconds)

### Option B: PowerShell Command
```powershell
& $Env:ProgramFiles\Docker\Docker\DockerCli.exe -SwitchDaemon
```

### Verify Windows Mode
```powershell
docker version

# Look for:
# Server: Docker Engine - Enterprise
#  OS/Arch:      windows/amd64
```

---

## Step 2: Build Windows Container

### In Visual Studio (Easiest)

1. Open `DocTranslationV2.sln`
2. Press **F5** (or Ctrl+F5 for no debugging)
3. Visual Studio will:
   - Detect Windows container configuration
   - Build the image (first time takes 10-20 minutes)
   - Start the container
   - Open browser to application

### Manual Build (PowerShell)

```powershell
# Navigate to solution directory
cd C:\Users\cbo\source\repos\DocTranslationV2

# Build Windows container
docker build -t doctranslationv2:windows -f DocTranslationV2/Dockerfile .

# This will take 10-20 minutes on first build
# Subsequent builds are faster due to layer caching
```

---

## Step 3: Run Container Locally

```powershell
# Run container
docker run -d `
  -p 8080:8080 `
  --name doctranslation `
  -e ASPNETCORE_ENVIRONMENT=Development `
  doctranslationv2:windows

# Verify it's running
docker ps

# View logs
docker logs -f doctranslation

# Test the endpoint
Start-Process "http://localhost:8080"
```

---

## Step 4: Test Visio/EMF Support

Upload a PowerPoint with a Visio diagram and check logs:

**Expected Output (Windows - Perfect):**
```
info: Detected metafile format image/x-emf for pptx_slide1_img0
info: Extracted EMF/WMF dimensions from PowerPoint metadata: 921x688
info: Converting image/x-emf to PNG (921x688) using ImageMagick
info: Successfully converted image/x-emf to PNG (125432 bytes)
info: Extracted image pptx_slide1_img0 [Converted EMF/WMF?PNG]
```

**No more warnings!** Visio diagrams render perfectly! ?

---

## Troubleshooting

### Error: "image operating system mismatch"

```
docker: image operating system "windows" cannot be used on this platform
```

**Fix:** Docker is in Linux mode. Switch to Windows containers (Step 1).

---

### Error: "Hns failed with error: The parameter is incorrect"

```
Error response from daemon: hcsshim::CreateComputeSystem: The parameter is incorrect.
```

**Fix:** 
1. Restart Docker Desktop
2. Restart Windows (if problem persists)
3. Ensure Hyper-V is enabled

---

### Error: "No matching manifest for windows/amd64"

```
no matching manifest for windows/amd64 in the manifest list entries
```

**Fix:** You're trying to pull a Linux image. Ensure you're building from `Dockerfile` (Windows) not `Dockerfile.linux`.

---

### Build is Very Slow (>20 minutes)

This is **normal** for Windows containers on first build. Subsequent builds are much faster due to layer caching.

**Tips to speed up:**
- ? Ensure good internet connection (downloading 5GB+ base image)
- ? Use SSD for Docker storage
- ? Don't interrupt first build
- ? Subsequent builds reuse layers (much faster)

---

### Container Crashes Immediately

**Check logs:**
```powershell
docker logs doctranslation
```

**Common issues:**
- Missing environment variables
- Port 8080 already in use
- Application configuration errors

---

## Production Deployment

### Azure App Service (Windows)

**1. Create Azure Container Registry:**
```powershell
az acr create `
  --name myregistry `
  --resource-group myResourceGroup `
  --sku Standard `
  --admin-enabled true
```

**2. Build and Push Image:**
```powershell
# Login to ACR
az acr login --name myregistry

# Build and push
docker build -t myregistry.azurecr.io/doctranslationv2:windows -f DocTranslationV2/Dockerfile .
docker push myregistry.azurecr.io/doctranslationv2:windows
```

**3. Create Windows App Service:**
```powershell
# Create Windows App Service Plan (P1V3 recommended)
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
  --deployment-container-image-name myregistry.azurecr.io/doctranslationv2:windows

# Configure ACR credentials
az webapp config container set `
  --name myApp `
  --resource-group myResourceGroup `
  --docker-custom-image-name myregistry.azurecr.io/doctranslationv2:windows `
  --docker-registry-server-url https://myregistry.azurecr.io `
  --docker-registry-server-user myregistry `
  --docker-registry-server-password (az acr credential show -n myregistry --query "passwords[0].value" -o tsv)
```

**4. Configure Application Settings:**
```powershell
az webapp config appsettings set `
  --name myApp `
  --resource-group myResourceGroup `
  --settings `
    ASPNETCORE_ENVIRONMENT=Production `
    AzureStorage__ConnectionString="<your-connection-string>" `
    AzureTranslation__Endpoint="<your-endpoint>" `
    AzureTranslation__Key="<your-key>"
```

---

## Monitoring and Logs

### View Container Logs (Local)
```powershell
# Follow logs in real-time
docker logs -f doctranslation

# Last 100 lines
docker logs --tail 100 doctranslation

# Since specific time
docker logs --since 30m doctranslation
```

### View Azure App Service Logs
```powershell
# Stream logs
az webapp log tail --name myApp --resource-group myResourceGroup

# Download logs
az webapp log download --name myApp --resource-group myResourceGroup
```

### Application Insights
Windows containers work perfectly with Application Insights. Check for:
- **EMF/WMF conversion success rate**
- **Image processing performance**
- **Translation operation status**

---

## Performance Expectations

### First Build
- **Time:** 10-20 minutes
- **Download:** ~5GB base images
- **Disk Space:** ~8GB

### Subsequent Builds
- **Time:** 2-5 minutes (layers cached)
- **Disk Space:** +500MB per build

### Runtime Performance
- **Cold Start:** ~15 seconds
- **Warm Start:** <2 seconds
- **Memory:** ~500MB baseline
- **EMF/WMF Conversion:** <500ms per image

---

## Cost Estimation (Azure)

### Development
- **App Service Plan:** P1V3 (~$175/month)
- **Storage:** ~$5/month
- **Total:** ~$180/month

### Production (with auto-scaling)
- **App Service Plan:** P2V3 (~$350/month)
- **Storage:** ~$20/month
- **Application Insights:** ~$30/month
- **Total:** ~$400/month

**Note:** Windows containers are ~3x more expensive than Linux, but provide perfect Visio support.

---

## Next Steps

? **Setup Complete!** Your Windows container is running with perfect Visio/EMF support.

**Recommended Actions:**
1. ? Test with sample PowerPoint containing Visio diagrams
2. ? Verify logs show successful EMF/WMF conversion (no warnings)
3. ? Set up Application Insights monitoring
4. ? Configure Azure App Service for production
5. ? Document cost vs. Linux alternative for stakeholders

**Need to switch back to Linux?** See [DOCKER_CONFIGURATION.md](./DOCKER_CONFIGURATION.md)

---

## Support

**Documentation:**
- [Container Platform Comparison](./CONTAINER_PLATFORM_COMPARISON.md)
- [Docker Configuration Guide](./DOCKER_CONFIGURATION.md)
- [PowerPoint EMF/WMF Technical Details](./POWERPOINT_EMF_WMF_FIX.md)

**Troubleshooting:**
- Check Application Insights for errors
- Review Docker logs: `docker logs doctranslation`
- Verify Windows container mode: `docker version`
