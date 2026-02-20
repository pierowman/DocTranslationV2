# Docker Build Instructions

## Rebuilding After Dockerfile Changes

After updating the Dockerfile to fix EMF/WMF support, you need to rebuild the Docker image:

### Option 1: Visual Studio (Recommended for Development)

1. **Stop the running container** (if any):
   - In Visual Studio, stop debugging (Shift+F5)

2. **Clean Docker resources**:
   - Open Visual Studio Developer PowerShell
   - Run:
     ```powershell
     docker-compose down --volumes --remove-orphans
     ```

3. **Rebuild and run**:
   - Press F5 or click "Docker" in the debug dropdown
   - Visual Studio will automatically rebuild the image with the new Dockerfile

### Option 2: Command Line

1. **Navigate to solution directory**:
   ```powershell
   cd C:\Users\cbo\source\repos\DocTranslationV2
   ```

2. **Build the Docker image**:
   ```powershell
   docker build -t doctranslationv2:latest -f DocTranslationV2/Dockerfile .
   ```

3. **Verify ImageMagick delegates are installed**:
   ```powershell
   docker run --rm doctranslationv2:latest dpkg -l | grep -E "imagemagick|libwmf"
   ```
   
   You should see:
   ```
   ii  imagemagick          8:6.9.11.60+dfsg-1.3+deb11u1
   ii  libmagickcore-6.q16-6-extra  8:6.9.11.60+dfsg-1.3+deb11u1
   ii  libwmf-0.2-7         0.2.12-5
   ii  libwmf-dev           0.2.12-5
   ```

4. **Check ImageMagick format support**:
   ```powershell
   docker run --rm doctranslationv2:latest convert -list format | grep -E "EMF|WMF"
   ```
   
   You should see:
   ```
   WMF* WMF       rw+   Windows Meta File
   EMF* EMF       rw+   Windows Enhanced Meta File
   ```

### Option 3: Docker Compose

If you're using Docker Compose:

```powershell
# Rebuild specific service
docker-compose build doctranslationv2

# Or rebuild all services
docker-compose build

# Start services
docker-compose up -d
```

## Verification

### 1. Check Container Logs
```powershell
docker logs <container-id> | grep ImageMagick
```

### 2. Test EMF/WMF Conversion
1. Upload a PowerPoint with EMF/WMF images
2. Watch Application Insights or container logs for:
   ```
   Successfully converted image/x-emf to PNG (125432 bytes)
   Extracted image pptx_slide1_img0_rId2 [Converted EMF/WMF?PNG]
   ```

### 3. If Conversion Still Fails

**Check policy.xml**:
```powershell
docker exec -it <container-id> cat /etc/ImageMagick-6/policy.xml | grep -E "WMF|EMF"
```

You should see:
```xml
<policy domain="coder" rights="read|write" pattern="WMF" />
<policy domain="coder" rights="read|write" pattern="EMF" />
```

**NOT:**
```xml
<policy domain="coder" rights="none" pattern="WMF" />
<policy domain="coder" rights="none" pattern="EMF" />
```

## Troubleshooting

### Issue: "No delegate for this image format"

**Solution:** The image rebuild didn't install delegates properly.

1. Force a complete rebuild:
   ```powershell
   docker build --no-cache -t doctranslationv2:latest -f DocTranslationV2/Dockerfile .
   ```

2. Verify packages are installed (see verification steps above)

### Issue: "Not authorized to convert"

**Solution:** ImageMagick policy wasn't updated.

1. Check if `sed` commands ran in build output
2. Manually fix policy inside running container (temporary):
   ```powershell
   docker exec -it <container-id> bash
   sed -i 's/<policy domain="coder" rights="none" pattern="WMF" \/>/<policy domain="coder" rights="read|write" pattern="WMF" \/>/g' /etc/ImageMagick-6/policy.xml
   sed -i 's/<policy domain="coder" rights="none" pattern="EMF" \/>/<policy domain="coder" rights="read|write" pattern="EMF" \/>/g' /etc/ImageMagick-6/policy.xml
   exit
   ```

3. Rebuild image with `--no-cache`

### Issue: Old image still being used

**Solution:** Clean Docker cache and rebuild:
```powershell
# Remove old images
docker rmi doctranslationv2:latest

# Clean build cache
docker builder prune -a -f

# Rebuild
docker build -t doctranslationv2:latest -f DocTranslationV2/Dockerfile .
```

## Production Deployment

### Azure Container Registry

1. **Tag image for ACR**:
   ```bash
   docker tag doctranslationv2:latest <registry-name>.azurecr.io/doctranslationv2:v1.0
   ```

2. **Push to ACR**:
   ```bash
   az acr login --name <registry-name>
   docker push <registry-name>.azurecr.io/doctranslationv2:v1.0
   ```

3. **Update App Service**:
   ```bash
   az webapp config container set \
     --name <app-name> \
     --resource-group <rg-name> \
     --docker-custom-image-name <registry-name>.azurecr.io/doctranslationv2:v1.0
   ```

### Azure App Service (Web App for Containers)

1. **Deploy from VS**:
   - Right-click project ? Publish
   - Select Azure App Service (Linux)
   - Configure container settings
   - Publish

2. **Verify deployment**:
   - Check App Service logs
   - Test with PowerPoint containing EMF/WMF
   - Monitor Application Insights

## Build Optimization

To speed up builds, use multi-stage caching:

```dockerfile
# Add to Dockerfile before base stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base-with-imagemagick
USER root
RUN apt-get update && apt-get install -y \
    imagemagick \
    libmagickcore-6.q16-6-extra \
    libwmf-0.2-7 \
    libwmf-dev \
    && rm -rf /var/lib/apt/lists/*
RUN sed -i 's/<policy domain="coder" rights="none" pattern="WMF" \/>/<policy domain="coder" rights="read|write" pattern="WMF" \/>/g' /etc/ImageMagick-6/policy.xml || true \
    && sed -i 's/<policy domain="coder" rights="none" pattern="EMF" \/>/<policy domain="coder" rights="read|write" pattern="EMF" \/>/g' /etc/ImageMagick-6/policy.xml || true
USER $APP_UID

# Then use it in base stage
FROM base-with-imagemagick AS base
WORKDIR /app
EXPOSE 8080
```

This caches the ImageMagick installation layer and speeds up subsequent builds.
