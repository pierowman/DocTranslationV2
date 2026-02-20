# DocTranslation

An ASP.NET Core (.NET 9) web application for translating documents using Azure AI Translator and Azure Blob Storage.

## Features

- Translate documents into multiple target languages simultaneously
- Batch (async) and single-document (sync) translation modes
- Image extraction and re-embedding for .pdf and .docx files
- Dynamically fetches supported languages from the Azure Translator API
- Job queue with real-time status polling
- Application Insights telemetry
- Docker support

## Supported File Types

| Format | Batch | Sync | Image Processing |
|--------|-------|------|-----------------|
| .pdf | Yes | Yes | Yes |
| .docx | Yes | Yes | Yes |
| .pptx | Yes | Yes | Yes |
| .xlsx | Yes | No | No |
| .txt | Yes | Yes | No |
| .html / .htm | Yes | Yes | No |
| .rtf | Yes | No | No |
| .odt / .ods / .odp | Yes | No | No |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- An [Azure AI Translator](https://learn.microsoft.com/en-us/azure/ai-services/translator/) resource
- An [Azure Storage Account](https://learn.microsoft.com/en-us/azure/storage/common/storage-account-overview)
- An Azure AD App Registration (for Blob Storage authentication)
- (Optional) [Docker](https://www.docker.com/) for containerized deployment
