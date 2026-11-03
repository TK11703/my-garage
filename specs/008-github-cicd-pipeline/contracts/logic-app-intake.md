# Contract: Logic App intake workflow

Tracked in `infra/logic-app/` (`New-IntakeLogicApp.ps1`, `workflow.definition.json`, `README.md`). **Not** part of the deployment pipeline: it is created and updated by the owner running the script.

## Script: `New-IntakeLogicApp.ps1`

| Parameter | Required | Default | Meaning |
|---|---|---|---|
| `-OutlookConnectionId` | yes | none | Resource ID of the existing `Microsoft.Web/connections` resource (API `outlook`) authorised for `mygarage-intake@outlook.com`; created and consented to by the owner |
| `-ResourceGroup` | no | `rg-apps` | |
| `-Location` | no | `northcentralus` | Must equal the connection's region |
| `-Name` | no | `logic-mygarage-intake` | |
| `-IdentityName` | no | `id-mygarage-intake` | User-assigned identity of the Logic App; created when missing |
| `-ApiAddress` | no | read from `ca-api-mygarage` | `https://<fqdn>` of the deployed API |
| `-ApiClientId` | no | read from the `MyGarage Azure API` registration | Audience is `api://<client id>` |
| `-PollIntervalMinutes` | no | `5` | Trigger recurrence; controls cost and latency |
| `-WhatIf` | no | off | Reports intended changes without making them |

Behaviour (each step is idempotent and reports `created`, `updated`, or `unchanged`):

1. Check prerequisites: signed in to Azure, the resource group and API exist, the connection exists, is in the same region, and shows status `Connected` (otherwise stop with instructions; the script never creates or consents to the connection).
2. Create the content-conversion connection `con-mygarage-conversion` when missing.
3. Create the user-assigned identity `id-mygarage-intake` when missing.
4. Create or update the Consumption Logic App from `workflow.definition.json` with the parameter values above, with that identity assigned; a hash of the desired state is kept in a tag so a repeat run reports `unchanged`.
5. Grant the identity's service principal the `Email.Ingest` app role on `MyGarage Azure API` via Microsoft Graph (`az rest`), skipping when already assigned.
6. Print the Logic App's name, state, and the trigger interval. No secret exists in this flow and none is printed.

Preconditions: the API is deployed (it reads the address from it); the person running it can assign app roles in the tenant (the same rights `infra/entra` needs).

## Workflow definition

| Element | Definition |
|---|---|
| Trigger | Outlook.com "When a new email arrives (V2)" (`/v2/Mail/OnNewEmail`, one run per email through `splitOn`): folder Inbox, include attachments, fetch all mail, recurrence set from `-PollIntervalMinutes` by the script |
| Text | The Content Conversion connector action "Html to text" (`/html2text`) through the connection `con-mygarage-conversion`, which the script creates (no sign-in or consent needed); the result is cut to 100,000 characters |
| Attachments | `Keep_usable_attachments` drops inline items and items over 10 MB (`Size` 10485760), then at most the first 5 are mapped; the API remains the authority and enforces all limits |
| Request mapping | `messageId` = `InternetMessageId`; `receivedAt` = `DateTimeReceived`; `from` = `From`; `subject` (cut to 500 characters); `bodyText`; `attachments[]` of `{ fileName = Name, contentType = ContentType, isInline: false, contentBase64 = ContentBytes }` |
| Call | `POST {ApiAddress}/api/intake/emails`, `Content-Type: application/json`, authentication **Managed identity** (user-assigned `id-mygarage-intake`), audience `api://<ApiClientId>` |
| Retry | The `Send_to_api` action uses an exponential retry policy (4 attempts, 10 second start, one minute cap). The platform policy retries timeouts, HTTP 408, 429 and 5xx; it never retries 400, 401, 403, or 413 |
| Failure handling | `202`: done. `413`: resend once without attachments (`Handle_failure`), then stop. Any other failure: the `Terminate` action leaves the run **Failed** so the owner sees it in the run history. Because `messageId` is an idempotency key, a repeated attempt is safe |
| Idempotency | The API treats `messageId` as an idempotency key, so resending a whole message is safe |
| Logging | The run history contains the email content (as for any Logic App); access is limited to subscription owners. Secure inputs and outputs are enabled for the HTTP action so content is hidden in run history |

The request and response shapes are exactly the existing contract in `infra/README.md` and [specs/007-email-ingestion-api/contracts/intake-email.example.json](../../007-email-ingestion-api/contracts/intake-email.example.json); this feature does not change them.

## Cost and safety notes

- Consumption pricing, standard connector; about one trigger poll every 5 minutes.
- The mailbox only ever receives forwarded mail from known users; unmatched senders are accepted by the API and discarded without disclosure (existing behaviour).
