// My Garage on the existing shared Azure resources. Deployed to resource group rg-apps (incremental mode only);
// storage, database, and AI access are created in rg-platform through modules. Shared resources are only referenced.
targetScope = 'resourceGroup'

param location string = 'northcentralus'

@description('Image tag for both apps: "sha-" followed by the full 40-character commit SHA.')
@minLength(44)
@maxLength(44)
param imageTag string

param tenantId string
param apiClientId string
param apiScope string
param webClientId string
param extractionEndpoint string
param extractionDeployment string

param environmentName string
param environmentResourceGroup string
param platformResourceGroup string
param registryServer string
param pullIdentityName string
param postgresServerName string
param aiAccountName string

var apiIdentityResourceId = resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', 'id-mygarage-api')
var webIdentityResourceId = resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', 'id-mygarage-web')

module identities 'modules/identities.bicep' = {
  name: 'identities'
  params: {
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: resourceGroup(platformResourceGroup)
  params: {
    location: location
    apiIdentityResourceId: apiIdentityResourceId
    apiPrincipalId: identities.outputs.api.principalId
  }
}

module database 'modules/platform-database.bicep' = {
  name: 'database'
  scope: resourceGroup(platformResourceGroup)
  params: {
    serverName: postgresServerName
  }
}

module aiAccess 'modules/ai-access.bicep' = {
  name: 'ai-access'
  scope: resourceGroup(platformResourceGroup)
  params: {
    aiAccountName: aiAccountName
    apiIdentityResourceId: apiIdentityResourceId
    apiPrincipalId: identities.outputs.api.principalId
  }
}

module apps 'modules/container-apps.bicep' = {
  name: 'container-apps'
  params: {
    location: location
    environmentName: environmentName
    environmentResourceGroup: environmentResourceGroup
    registryServer: registryServer
    pullIdentityName: pullIdentityName
    pullIdentityResourceGroup: platformResourceGroup
    apiIdentityResourceId: apiIdentityResourceId
    apiIdentityClientId: identities.outputs.api.clientId
    webIdentityResourceId: webIdentityResourceId
    webIdentityClientId: identities.outputs.web.clientId
    imageTag: imageTag
    tenantId: tenantId
    apiClientId: apiClientId
    apiScope: apiScope
    webClientId: webClientId
    extractionEndpoint: extractionEndpoint
    extractionDeployment: extractionDeployment
    storageServiceUri: storage.outputs.serviceUri
    postgresFqdn: database.outputs.serverFqdn
    databaseName: database.outputs.databaseName
  }
  dependsOn: [
    aiAccess
  ]
}

output apiAddress string = apps.outputs.apiAddress
output webAddress string = apps.outputs.webAddress
output storageServiceUri string = storage.outputs.serviceUri
output apiRevisionName string = apps.outputs.apiRevisionName
output webRevisionName string = apps.outputs.webRevisionName
