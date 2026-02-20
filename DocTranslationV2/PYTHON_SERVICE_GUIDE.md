# Python PDF Service Integration Guide

## Overview

The Document Translation application uses a **hybrid microservices approach** for image processing:

- **Word Documents (.docx)**: Processed in .NET using DocumentFormat.OpenXml ?
- **PDF Documents (.pdf)**: Optionally processed using Python PyMuPDF service ??

## Architecture Decision

### Why Keep Word in .NET?
? **Already works perfectly** - RelationshipId-based tracking is production-ready  
? **No external dependencies** - Pure .NET with DocumentFormat.OpenXml  
? **Better performance** - No network calls, direct memory access  
? **Simpler deployment** - Fewer moving parts  
? **Well-maintained** - Microsoft official library  

### Why Python for PDF?
? **PyMuPDF superior** - Better PDF manipulation than iText7  
? **Accurate positioning** - Precise X/Y coordinate handling  
? **Free & open-source** - No licensing issues (vs iText7 AGPL)  
? **Proven solution** - Industry-standard library  
? **Isolated complexity** - PDF processing in separate service  

---

## Service Architecture

```
???????????????????????????????????????????????????????????
?           .NET Application (Port 5001)                   ?
?                                                           ?
?  ????????????????????????????????????????????????????  ?
?  ?    ImageExtractionService                         ?  ?
?  ?                                                    ?  ?
?  ?  Word Documents (.docx):                          ?  ?
?  ?  ? Extract images (OpenXML)                      ?  ?
?  ?  ? Replace images (RelationshipId)               ?  ?
?  ?  ? Processed in-process (fast)                    ?  ?
?  ?                                                    ?  ?
?  ?  PDF Documents (.pdf):                            ?  ?
?  ?  ? Extract images (iText7)                       ?  ?
?  ?  ? Calls PythonPdfService for replacement         ?  ?
?  ????????????????????????????????????????????????????  ?
?                 ?                                        ?
????????????????????????????????????????????????????????????
                  ? HTTP POST
                  ? /replace-images
                  ?
???????????????????????????????????????????????????????????
?        Python PDF Service (Port 5000)                    ?
?                                                           ?
?  ????????????????????????????????????????????????????  ?
?  ?  Flask API + PyMuPDF (fitz)                       ?  ?
?  ?                                                    ?  ?
?  ?  ? Receives translated PDF                       ?  ?
?  ?  ? Receives image mappings (positions)           ?  ?
?  ?  ? Receives translated image files               ?  ?
?  ?  ? Replaces images at exact X/Y coordinates      ?  ?
?  ?  ? Returns final PDF                             ?  ?
?  ????????????????????????????????????????????????????  ?
???????????????????????????????????????????????????????????
```

---

## Setup Instructions

### Option 1: Docker Compose (Recommended)

**Prerequisites:**
- Docker Desktop installed
- Docker Compose available

**Steps:**

1. **Build and start services:**
```bash
docker-compose up --build
```

2. **Access application:**
- .NET App: https://localhost:5001
- Python Service Health: http://localhost:5000/health

3. **Stop services:**
```bash
docker-compose down
```

### Option 2: Manual Setup

#### A. Start Python PDF Service

```bash
cd PythonPdfService

# Create virtual environment
python -m venv venv

# Activate (Windows)
venv\Scripts\activate
# Activate (Linux/Mac)
source venv/bin/activate

# Install dependencies
pip install -r requirements.txt

# Run service
python pdf_service.py
```

Service will start on `http://localhost:5000`

#### B. Start .NET Application

```bash
cd DocTranslationV2

# Update appsettings.json
# Set PythonPdfService:Enabled = true
# Set PythonPdfService:Url = http://localhost:5000

# Run application
dotnet run
```

Application will start on `https://localhost:5001`

---

## Configuration

### .NET Application (`appsettings.json`)

```json
{
  "PythonPdfService": {
    "Enabled": true,              // Enable Python service
    "Url": "http://localhost:5000", // Python service URL
    "TimeoutSeconds": 120          // HTTP timeout
  }
}
```

**Options:**
- `Enabled: false` - PDF image replacement disabled (fallback mode)
- `Enabled: true` - Uses Python service for PDF processing

---

## API Contract

### Python Service Endpoint

**POST** `/replace-images`

**Request (multipart/form-data):**
- `translated_pdf` (file): PDF with translated text
- `image_mappings` (JSON string): Array of image positions
- `translated_images` (files): Translated image files

**image_mappings format:**
```json
[
  {
    "page_number": 0,      // 0-indexed
    "x": 100,              // X coordinate
    "y": 200,              // Y coordinate
    "width": 400,          // Image width
    "height": 300,         // Image height
    "image_id": "pdf_page1_img0",
    "image_index": 0       // Index in translated_images array
  }
]
```

**Response:**
- `200 OK`: PDF file with images replaced
- `400 Bad Request`: Invalid request
- `500 Internal Server Error`: Processing error

### Health Check

**GET** `/health`

**Response:**
```json
{
  "status": "healthy",
  "service": "PDF Image Replacement"
}
```

---

## Testing

### 1. Test Python Service Directly

```bash
curl http://localhost:5000/health
```

Expected: `{"status": "healthy", "service": "PDF Image Replacement"}`

### 2. Test PDF Translation with Images

1. Upload a PDF with images
2. Select target language
3. Start translation
4. Check logs for:
   ```
   [INFO] Using Python service for PDF image replacement
   [INFO] Calling Python PDF service to replace X images
   [INFO] Successfully replaced X images in PDF using Python service
   ```

### 3. Fallback Testing

1. Stop Python service
2. Try PDF translation
3. Should see:
   ```
   [WARNING] Python PDF service is disabled. Returning PDF without image replacement.
   ```
4. Translation succeeds but images not replaced

---

## Monitoring & Logging

### .NET Application Logs

```
[INFO] Using Python service for PDF image replacement
[INFO] Calling Python PDF service to replace 3 images
[INFO] Successfully replaced 3 images in PDF using Python service
```

**Errors:**
```
[ERROR] Python PDF service returned error: 500 - ...
[ERROR] Error calling Python PDF service. Returning PDF without image replacement.
```

### Python Service Logs

```
INFO:__main__:Starting PDF Image Replacement Service
INFO:__main__:Received image replacement request
INFO:__main__:Processing 3 image mappings
INFO:__main__:Opened PDF with 5 pages
INFO:__main__:Replaced image 0 on page 0 at (100, 200, 400, 300)
INFO:__main__:Successfully replaced 3/3 images
```

---

## Deployment

### Development
```bash
# Terminal 1: Python service
cd PythonPdfService
python pdf_service.py

# Terminal 2: .NET app
cd DocTranslationV2
dotnet run
```

### Docker Compose
```bash
docker-compose up -d
```

### Kubernetes

**Python Service:**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: pdf-service
spec:
  replicas: 2
  selector:
    matchLabels:
      app: pdf-service
  template:
    metadata:
      labels:
        app: pdf-service
    spec:
      containers:
      - name: pdf-service
        image: your-registry/pdf-service:latest
        ports:
        - containerPort: 5000
        livenessProbe:
          httpGet:
            path: /health
            port: 5000
          initialDelaySeconds: 10
          periodSeconds: 30
```

### Azure Container Apps

```bash
# Deploy Python service
az containerapp create \
  --name pdf-service \
  --resource-group myResourceGroup \
  --image your-registry/pdf-service:latest \
  --target-port 5000 \
  --ingress internal

# Deploy .NET app with environment variable
az containerapp create \
  --name web-app \
  --resource-group myResourceGroup \
  --image your-registry/web-app:latest \
  --target-port 80 \
  --env-vars \
    PythonPdfService__Enabled=true \
    PythonPdfService__Url=http://pdf-service:5000
```

---

## Troubleshooting

### Issue: Python service not starting

**Check:**
```bash
cd PythonPdfService
pip list  # Verify PyMuPDF installed
python pdf_service.py  # Check for errors
```

**Solution:**
```bash
pip install --upgrade PyMuPDF flask pillow
```

### Issue: Connection refused from .NET app

**Check:**
- Python service running: `curl http://localhost:5000/health`
- Firewall not blocking port 5000
- URL in appsettings.json correct

### Issue: Images not replaced

**Check .NET logs:**
```
PythonPdfService:Enabled = true?
Service URL correct?
```

**Check Python logs:**
```
Are image_mappings being received?
Are images being received?
Check for errors in image replacement
```

### Issue: Timeout errors

**Increase timeout:**
```json
{
  "PythonPdfService": {
    "TimeoutSeconds": 300  // 5 minutes
  }
}
```

---

## Performance Considerations

### Python Service
- **Startup time:** ~2 seconds
- **Per-image processing:** ~0.1-0.5 seconds
- **Network overhead:** ~50-200ms per request

### Scaling
- Python service is **stateless** - can scale horizontally
- Use load balancer for multiple instances
- Consider caching for repeated operations

---

## Security

### Network Security
- Python service should be on **internal network** only
- Use HTTPS between services in production
- Implement API key authentication if needed

### Docker Security
```dockerfile
# Run as non-root user
RUN useradd -m appuser
USER appuser
```

### Rate Limiting
```python
from flask_limiter import Limiter

limiter = Limiter(app, key_func=lambda: request.remote_addr)

@app.route('/replace-images', methods=['POST'])
@limiter.limit("10 per minute")
def replace_images():
    # ...
```

---

## Summary

| Aspect | Word Documents | PDF Documents |
|--------|----------------|---------------|
| **Processing** | .NET (in-process) | Python microservice |
| **Library** | DocumentFormat.OpenXml | PyMuPDF (fitz) |
| **Performance** | Fast (no network) | Slight overhead (HTTP) |
| **Accuracy** | ? Excellent | ? Excellent |
| **Deployment** | Single process | Two services |
| **Fallback** | N/A | Returns PDF without images |

**Best of both worlds:** Fast in-process Word handling + powerful Python PDF processing! ??
