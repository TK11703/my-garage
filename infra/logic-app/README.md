# Email intake Logic App

`New-IntakeLogicApp.ps1` creates the Consumption Logic App that turns each email in the intake mailbox into one call to `POST <api>/api/intake/emails`. It is **not** part of the deployment pipeline: you run it, and you re-run it to update the app. The request and response contract is in [`infra/README.md`](../README.md#logic-app-contract).

## What it creates

| Resource | Name | Notes |
|---|---|---|
| Logic App (Consumption) | `logic-mygarage-intake` in `rg-apps`, `northcentralus` | Definition from [`workflow.definition.json`](workflow.definition.json); polls every 5 minutes by default |
| User-assigned identity | `id-mygarage-intake` | The Logic App's identity; holds the `Email.Ingest` app role on `MyGarage Azure API` |
| API connection | `con-mygarage-conversion` | Content Conversion connector used for "Html to text"; needs no sign-in |

It never creates or consents to the Outlook connection: you provide that.

## Before you run it

1. The API is deployed (the script reads the address of `ca-api-mygarage`).
2. Create the Outlook.com connection for the intake mailbox (`mygarage-intake@outlook.com`) in region `northcentralus`: in the Azure portal create an API connection of type *Outlook.com* (for example in `rg-apps`), then open it and authorise it by signing in as the mailbox owner. It must show status *Connected*.
3. `az login` as a user who can create resources in `rg-apps` and assign app roles in the tenant (the same rights `infra/entra` needs).

## Run it

```powershell
./infra/logic-app/New-IntakeLogicApp.ps1 -OutlookConnectionId <resource id of the connection> -WhatIf
./infra/logic-app/New-IntakeLogicApp.ps1 -OutlookConnectionId <resource id of the connection>
```

Each step reports `created`, `updated`, or `unchanged`; a second run changes nothing.

## Parameters

| Parameter | Default | Description |
|---|---|---|
| `-OutlookConnectionId` | required | Resource id of the Outlook connection |
| `-ResourceGroup` | `rg-apps` | Group of the Logic App, identity, and conversion connection |
| `-Location` | `northcentralus` | Must match the connection's region |
| `-Name` | `logic-mygarage-intake` | Logic App name |
| `-IdentityName` | `id-mygarage-intake` | User-assigned identity |
| `-ApiAddress` | address of `ca-api-mygarage` | `https://` address of the API |
| `-ApiClientId` | client id of `MyGarage Azure API` | The token audience is `api://<client id>` |
| `-PollIntervalMinutes` | `5` | How often the mailbox is checked (cost and latency) |
| `-WhatIf` | off | Reports what would change |

## Behaviour

The mapping, attachment limits, retry policy, and failure handling are described in the feature contract ([`specs/008-github-cicd-pipeline/contracts/logic-app-intake.md`](../../specs/008-github-cicd-pipeline/contracts/logic-app-intake.md)). In short: inline images and files over 10 MB are dropped, at most 5 attachments are sent, a `413` is retried once without attachments, any other rejection leaves the run **Failed** in the run history, and inputs and outputs of the call are hidden from the run history because they contain email content.

## Cost

Consumption plan with standard connectors: roughly one trigger poll every 5 minutes, which is well under a dollar a month.
