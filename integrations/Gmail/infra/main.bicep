param projectName string
param integrationName string
param sharedAppServicePlanName string
param sharedStorageAccountName string
param sharedKeyVaultName string
param sharedServiceBusNamespaceName string
param sharedAppInsightsName string
param timeZone string
param smtpHost string
param smtpPort string
param smtpUserSecretName string
param smtpAllowedRecipientsSecretName string
param smtpPasswordSecretName string
param smtpFromName string
param smtpUseSsl string

module functionApp '../../../infrastructure/modules/functionApp.bicep' = {
  name: 'functionAppDeploy'
  params: {
    projectName: projectName
    integrationName: integrationName
    customAppSettings: [
      {
        name: 'Smtp__Host'
        value: smtpHost
      }
      {
        name: 'Smtp__Port'
        value: smtpPort
      }
      {
        name: 'Smtp__User'
        value: '@Microsoft.KeyVault(VaultName=${sharedKeyVaultName};SecretName=${smtpUserSecretName})'
      }
      {
        name: 'Smtp__Password'
        value: '@Microsoft.KeyVault(VaultName=${sharedKeyVaultName};SecretName=${smtpPasswordSecretName})'
      }
      {
        name: 'Smtp__From'
        value: '@Microsoft.KeyVault(VaultName=${sharedKeyVaultName};SecretName=${smtpUserSecretName})'
      }
      {
        name: 'Smtp__AllowedRecipients'
        value: '@Microsoft.KeyVault(VaultName=${sharedKeyVaultName};SecretName=${smtpAllowedRecipientsSecretName})'
      }
      {
        name: 'Smtp__FromName'
        value: smtpFromName
      }
      {
        name: 'Smtp__UseSsl'
        value: smtpUseSsl
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
