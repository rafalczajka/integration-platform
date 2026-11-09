import { getResourceName, getUniqueResourceName } from '../lib/naming.bicep'

param projectName string
param integrationName string
param location string = resourceGroup().location

var name = '${projectName}-${integrationName}'

module storageAccount '../modules/storageAccount.bicep' = {
  name: 'storageAccountDeploy'
  params: {
    projectName: projectName
    integrationName: integrationName
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2026-05-15' = {
  name: getUniqueResourceName('kv', name)
  location: location
  properties: {
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enableRbacAuthorization: false
    tenantId: subscription().tenantId
    sku: {
      name: 'standard'
      family: 'A'
    }
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: '51db1598-f913-46fa-970a-65ae40722481'
        permissions: {
          secrets: ['set', 'get', 'list', 'delete']
        }
      }
    ]
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: getResourceName('asp', name)
  kind: 'functionapp'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: getResourceName('log', name)
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: getResourceName('appi', name)
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: getResourceName('sbns', name)
  location: location
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}
