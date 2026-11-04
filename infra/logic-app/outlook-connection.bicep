targetScope = 'resourceGroup'

@description('Name of the Outlook.com API connection used by the intake Logic App.')
param connectionName string = 'con-mygarage-outlook'

param location string = resourceGroup().location

// Created unauthorised: the mailbox owner signs in once in the portal. Run only when the connection is missing,
// because redeploying it can reset an authorised connection.
resource connection 'Microsoft.Web/connections@2016-06-01' = {
  name: connectionName
  location: location
  properties: {
    displayName: 'My Garage intake mailbox'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'outlook')
    }
  }
}

output connectionId string = connection.id
