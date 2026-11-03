// Database for the app on the existing shared PostgreSQL server. The server, its roles, and its settings are not managed here.
targetScope = 'resourceGroup'

param serverName string
param databaseName string = 'mygarage'

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' existing = {
  name: serverName
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2025-08-01' = {
  parent: server
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

output databaseName string = database.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
