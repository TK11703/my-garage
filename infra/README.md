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

## Email ingestion

The same storage account also holds two more **private** containers, `email-intake` (attachments until their draft is decided or expires) and `vehicle-documents` (originals users keep with a record). The API creates them at startup; the same managed identity and role as above apply. Enable *Defender for Storage* on the account so uploaded files are scanned for malware. Extraction needs an Azure OpenAI resource: set `EmailIntake:Extraction:Endpoint` and `EmailIntake:Extraction:Deployment` and give the API's identity the *Cognitive Services OpenAI User* role. The Entra side (the `Email.Ingest` app role and granting it to the workflow) is in [entra/README.md](entra/README.md).

### Logic App contract

The Logic App turns each incoming email into one JSON request and calls the API:

- `POST {api}/api/intake/emails` with header `Content-Type: application/json` and authentication type *Managed identity* (audience `api://<api-client-id>`). The identity must hold the `Email.Ingest` app role; a user token or any other role gets `403`.
- Body: see [`specs/007-email-ingestion-api/contracts/intake-email.example.json`](../specs/007-email-ingestion-api/contracts/intake-email.example.json). Required: `messageId` (unique per email, up to 512 characters; it is the idempotency key, so sending the same email again is safe), `receivedAt`, `from` (the sender address, up to 320 characters), `subject` (up to 500), and `bodyText` (plain text, up to 100,000 characters; convert HTML to text in the workflow). Optional `attachments`: at most 5 files that are not inline images (inline images such as signature logos are flagged `isInline: true` and ignored), each `fileName`, `contentType`, and `contentBase64`, up to 10 MB each and 15 MB in total after decoding. Only PDF, JPEG, PNG, and WebP files are read; the type is decided from the file bytes, not from the name or `contentType`.
- Answer: `202` with `{ "submissionId": "...", "status": "received" }` for every valid request, whether or not the sender matches a user and whether or not the message id was seen before. The body never reveals whether a user exists.
- Retry policy: retry on `429` (honor `Retry-After`; the endpoint allows 120 requests per minute) and `503` (honor `Retry-After`; storage is temporarily unavailable), with exponential backoff and a small number of attempts. Because the message id makes calls idempotent, retrying the whole message is safe. **Do not retry** `400` (fix the request: it names the invalid field), `401` and `403` (fix the identity or role), or `413` (the message is too large; drop or shrink attachments).
- No polling: the workflow never reads results. Processing happens after the `202`; the owner is notified inside the app and reviews drafts there.
