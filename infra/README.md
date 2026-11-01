# Infrastructure

Use this folder for infrastructure-as-code templates, environment configuration, and scripts that provision and deploy the application's supporting resources.

Never commit secrets or credentials; reference secure configuration sources instead.

| Folder | Contents |
|---|---|
| [entra/](entra/README.md) | Script and instructions that register the web app and API in a Microsoft Entra tenant and populate the AppHost user-secrets |

## Azure Storage for vehicle pictures

Locally the AppHost runs the Azurite emulator, so nothing needs provisioning. In Azure the API needs a storage account (no templates exist yet) configured as follows:

- Blob container `vehicle-images`, **private** (anonymous access disabled on the account and the container). The API creates the container at startup when it has permission; infrastructure may create it instead.
- Shared-key access disabled and HTTPS only. The API authenticates with its **managed identity** (`DefaultAzureCredential`), which needs the *Storage Blob Data Contributor* role on the account (or container).
- Configuration read by the Aspire Azure Blob client: `ConnectionStrings:blobs` (a service URI such as `https://<account>.blob.core.windows.net/`) or `Aspire:Azure:Storage:Blobs:ServiceUri`.
- No SAS tokens or storage URLs are ever returned to browsers; pictures are streamed by the API through the web app.

The PostgreSQL database is expected as connection string `ConnectionStrings:garagedb`; the API applies its migrations at startup.
