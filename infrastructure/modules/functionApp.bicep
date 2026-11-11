import { getResourceName } from '../lib/naming.bicep'

param projectName string
param integrationName string
param customAppSettings array
param sharedAppServicePlanName string
param sharedStorageAccountName string
param sharedKeyVaultName string
param sharedServiceBusNamespaceName string
param sharedAppInsightsName string
param timeZone string
param location string = resourceGroup().location

var name = '${projectName}-${integrationName}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: sharedStorageAccountName
}

resource keyVault 'Microsoft.KeyVault/vaults@2026-05-15' existing = {
  name: sharedKeyVaultName
}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-11-01' existing = {
  name: sharedAppServicePlanName
}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: sharedServiceBusNamespaceName
}

resource serviceBusAuthorizationRules 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2024-01-01' existing = {
  name: 'RootManageSharedAccessKey'
  parent: serviceBusNamespace
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: sharedAppInsightsName
}

resource functionApp 'Microsoft.Web/sites@2024-11-01' = {
  name: getResourceName('func', name)
  kind: 'functionapp'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      netFrameworkVersion: 'v11.0'
      cors: {
        allowedOrigins: [
          'https://portal.azure.com'
        ]
        supportCredentials: true
      }
      appSettings: concat([
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'WEBSITE_TIME_ZONE'
          value: timeZone
        }
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'ServiceBusConnectionString'
          value: serviceBusAuthorizationRules.listKeys().primaryConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: 'InstrumentationKey=${applicationInsights.properties.InstrumentationKey}'
        }
      ], customAppSettings)
    }
    httpsOnly: true
  }
}

resource keyVaultAccessPolicies 'Microsoft.KeyVault/vaults/accessPolicies@2026-05-15' = {
  name: 'add'
  parent: keyVault
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: functionApp.identity.principalId
        permissions: {
          secrets: ['get']
        }
      }
    ]
  }
}

output appName string = functionApp.name
