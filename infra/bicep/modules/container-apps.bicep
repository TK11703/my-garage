// The API and web Container Apps in the existing shared Container Apps environment.
targetScope = 'resourceGroup'

param location string
param environmentName string
param environmentResourceGroup string
param registryServer string
param pullIdentityName string
param pullIdentityResourceGroup string

param apiIdentityResourceId string
param apiIdentityClientId string
param webIdentityResourceId string
param webIdentityClientId string

@minLength(44)
@maxLength(44)
param imageTag string

param tenantId string
param apiClientId string
param apiScope string
param webClientId string
param extractionEndpoint string
param extractionDeployment string
param storageServiceUri string
param postgresFqdn string
param databaseName string = 'mygarage'

var apiAppName = 'ca-api-mygarage'
var webAppName = 'ca-web-mygarage'
var databaseUser = last(split(apiIdentityResourceId, '/'))

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' existing = {
  name: environmentName
  scope: resourceGroup(environmentResourceGroup)
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: pullIdentityName
  scope: resourceGroup(pullIdentityResourceGroup)
}

var probePort = 8080

var probes = [
  {
    type: 'Startup'
    httpGet: { path: '/alive', port: probePort }
    periodSeconds: 5
    timeoutSeconds: 3
    failureThreshold: 60
  }
  {
    type: 'Liveness'
    httpGet: { path: '/alive', port: probePort }
    periodSeconds: 30
    timeoutSeconds: 3
    failureThreshold: 3
  }
  {
    type: 'Readiness'
    httpGet: { path: '/health', port: probePort }
    periodSeconds: 10
    timeoutSeconds: 5
    failureThreshold: 3
  }
]

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: apiAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityResourceId}': {}
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: probePort
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registryServer
          identity: pullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${registryServer}/mygarage/api:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: probes
          env: [
            { name: 'AZURE_CLIENT_ID', value: apiIdentityClientId }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AzureAd__TenantId', value: tenantId }
            { name: 'AzureAd__Api__ClientId', value: apiClientId }
            { name: 'AzureAd__Api__Scope', value: apiScope }
            { name: 'Database__UseEntraAuthentication', value: 'true' }
            // No password: the API signs in to PostgreSQL with a Microsoft Entra token for its managed identity.
            { name: 'ConnectionStrings__garagedb', value: 'Host=${postgresFqdn};Database=${databaseName};Username=${databaseUser};SSL Mode=Require;Maximum Pool Size=10' }
            { name: 'ConnectionStrings__blobs', value: storageServiceUri }
            { name: 'EmailIntake__Extraction__Endpoint', value: extractionEndpoint }
            { name: 'EmailIntake__Extraction__Deployment', value: extractionDeployment }
            { name: 'ReleaseId', value: imageTag }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
        cooldownPeriod: 300
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2025-01-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webIdentityResourceId}': {}
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: probePort
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registryServer
          identity: pullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${registryServer}/mygarage/web:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: probes
          env: [
            { name: 'AZURE_CLIENT_ID', value: webIdentityClientId }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'AzureAd__TenantId', value: tenantId }
            { name: 'AzureAd__Web__ClientId', value: webClientId }
            // The web app proves its identity with this managed identity's federated credential instead of a client secret.
            { name: 'AzureAd__Web__ManagedIdentityClientId', value: webIdentityClientId }
            { name: 'AzureAd__Api__ClientId', value: apiClientId }
            { name: 'AzureAd__Api__Scope', value: apiScope }
            { name: 'services__api__https__0', value: 'https://${api.properties.configuration.ingress.fqdn}' }
            { name: 'ReleaseId', value: imageTag }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        // One replica at most: sessions are valid only in the process that created them.
        maxReplicas: 1
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

output apiAddress string = 'https://${api.properties.configuration.ingress.fqdn}'
output webAddress string = 'https://${web.properties.configuration.ingress.fqdn}'
output apiRevisionName string = api.properties.latestRevisionName
output webRevisionName string = web.properties.latestRevisionName
