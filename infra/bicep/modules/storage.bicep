// Storage account for the private blob containers (vehicle images, email attachments, kept documents).
// The API creates the containers at start-up; this module creates the account and the API identity's data access.
targetScope = 'resourceGroup'

param location string

@description('Resource id of the API identity; seeds the role assignment name so it is known when the deployment starts.')
param apiIdentityResourceId string

param apiPrincipalId string

var accountName = 'stmygarage${take(uniqueString(subscription().id, 'mygarage'), 8)}'
var storageBlobDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource account 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: accountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    defaultToOAuthAuthentication: true
  }
}

resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, apiIdentityResourceId, storageBlobDataContributor)
  scope: account
  properties: {
    roleDefinitionId: storageBlobDataContributor
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output accountName string = account.name
output serviceUri string = account.properties.primaryEndpoints.blob
