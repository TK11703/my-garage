using 'main.bicep'

// Values come from the environment so no secret or tenant-specific value is stored in the file. In the pipeline they are the
// GitHub environment variables of "production"; IMAGE_TAG is set by the workflow.
param imageTag = readEnvironmentVariable('IMAGE_TAG')
param tenantId = readEnvironmentVariable('AZURE_TENANT_ID')
param apiClientId = readEnvironmentVariable('API_ENTRA_CLIENT_ID')
param apiScope = readEnvironmentVariable('API_ENTRA_SCOPE')
param webClientId = readEnvironmentVariable('WEB_ENTRA_CLIENT_ID')
param extractionEndpoint = readEnvironmentVariable('EXTRACTION_ENDPOINT')
param extractionDeployment = readEnvironmentVariable('EXTRACTION_DEPLOYMENT')

param location = 'northcentralus'
param environmentName = 'cae-shared'
param environmentResourceGroup = 'rg-apps'
param platformResourceGroup = 'rg-platform'
param registryServer = 'acccrshared.azurecr.io'
param pullIdentityName = 'id-shared-acrpull'
param postgresServerName = 'accpsqlshared'
param aiAccountName = 'aif-shared-acc'
