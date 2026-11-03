// Managed identities of the deployed apps. Also deployed once, on its own, by infra/azure/Initialize-Azure.ps1.
targetScope = 'resourceGroup'

param location string

resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-mygarage-api'
  location: location
}

resource web 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-mygarage-web'
  location: location
}

output api object = {
  resourceId: api.id
  clientId: api.properties.clientId
  principalId: api.properties.principalId
}

output web object = {
  resourceId: web.id
  clientId: web.properties.clientId
  principalId: web.properties.principalId
}
