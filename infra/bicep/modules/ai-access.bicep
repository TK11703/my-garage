// Lets the API identity call the shared AI account for document extraction. The account itself is not managed here.
targetScope = 'resourceGroup'

param aiAccountName string

@description('Resource id of the API identity; seeds the role assignment name so it is known when the deployment starts.')
param apiIdentityResourceId string

param apiPrincipalId string

var cognitiveServicesOpenAiUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: aiAccountName
}

resource openAiAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, apiIdentityResourceId, cognitiveServicesOpenAiUser)
  scope: account
  properties: {
    roleDefinitionId: cognitiveServicesOpenAiUser
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}
