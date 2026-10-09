param projectName string
param integrationName string
param sharedAppServicePlanName string
param sharedStorageAccountName string
param sharedKeyVaultName string
param sharedServiceBusNamespaceName string
param sharedAppInsightsName string
param notificationsEnabled bool
param notificationsBaseUrl string
param notificationsFunctionKeySecretName string
param todoistApiKeySecretName string
param todoistApiBaseUrl string
param enforceRulesSchedule string
param weeklyReportSchedule string
param todoistRecurringProjectId string
param timeZone string

module functionApp '../../../infrastructure/modules/functionApp.bicep' = {
  name: 'functionAppDeploy'
  params: {
    projectName: projectName
    integrationName: integrationName
    customAppSettings: [
      {
        name: 'TodoistApiBaseUrl'
        value: todoistApiBaseUrl
      }
      {
        name: 'TodoistApiKey'
        value: '@Microsoft.KeyVault(VaultName=${sharedKeyVaultName};SecretName=${todoistApiKeySecretName})'
      }
      {
        name: 'Notifications__Enabled'
        value: notificationsEnabled
      }
      {
        name: 'Notifications__BaseUrl'
        value: notificationsBaseUrl
      }
      {
        name: 'Notifications__FunctionKey'
        value: '@Microsoft.KeyVault(VaultName=${sharedKeyVaultName};SecretName=${notificationsFunctionKeySecretName})'
      }
      {
        name: 'EnforceRulesSchedule'
        value: enforceRulesSchedule
      }
      {
        name: 'WeeklyReportSchedule'
        value: weeklyReportSchedule
      }
      {
        name: 'TodoistProjectIds__Recurring'
        value: todoistRecurringProjectId
      }
    ]
    sharedAppServicePlanName: sharedAppServicePlanName
    sharedStorageAccountName: sharedStorageAccountName
    sharedKeyVaultName: sharedKeyVaultName
    sharedServiceBusNamespaceName: sharedServiceBusNamespaceName
    sharedAppInsightsName: sharedAppInsightsName
    timeZone: timeZone
  }
}

output appName string = functionApp.outputs.appName
