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
param driveWorkJsonCredentialsSecretName string
param drivePersonalJsonCredentialsSecretName string
param googleApplicationName string
param concurrentDownloads int
param accountingRecordsBackupCronSchedule string
param accountingRecordsExportFolderId string
param accountingRecordsBackupFolderId string
param accountingRecordsBackupFileNamePrefix string
param henrySavesBackupCronSchedule string
param henrySavesExportFolderId string
param henrySavesBackupFolderId string
param henrySavesBackupFileNamePrefix string
param timeZone string

resource keyVault 'Microsoft.KeyVault/vaults@2026-05-15' existing = {
  name: sharedKeyVaultName
}

module functionApp '../../../infrastructure/modules/functionApp.bicep' = {
  name: 'functionAppDeploy'
  params: {
    projectName: projectName
    integrationName: integrationName
    customAppSettings: [
      {
        name: 'AccountingRecordsBackupCronSchedule'
        value: accountingRecordsBackupCronSchedule
      }
      {
        name: 'HenrySavesBackupCronSchedule'
        value: henrySavesBackupCronSchedule
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
        value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${notificationsFunctionKeySecretName})'
      }
      {
        name: 'GoogleDrive__0__AccountType'
        value: 'Work'
      }
      {
        name: 'GoogleDrive__0__ApplicationName'
        value: googleApplicationName
      }
      {
        name: 'GoogleDrive__0__JsonCredentials'
        value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${driveWorkJsonCredentialsSecretName})'
      }
      {
        name: 'GoogleDrive__0__ConcurrentDownloads'
        value: concurrentDownloads
      }
      {
        name: 'GoogleDrive__1__AccountType'
        value: 'Personal'
      }
      {
        name: 'GoogleDrive__1__ApplicationName'
        value: googleApplicationName
      }
      {
        name: 'GoogleDrive__1__JsonCredentials'
        value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${drivePersonalJsonCredentialsSecretName})'
      }
      {
        name: 'GoogleDrive__1__ConcurrentDownloads'
        value: concurrentDownloads
      }
      {
        name: 'Backup__0__BackupType'
        value: 'AccountingRecords'
      }
      {
        name: 'Backup__0__AccountType'
        value: 'Work'
      }
      {
        name: 'Backup__0__ExportFolderId'
        value: accountingRecordsExportFolderId
      }
      {
        name: 'Backup__0__BackupFolderId'
        value: accountingRecordsBackupFolderId
      }
      {
        name: 'Backup__0__FileNamePrefix'
        value: accountingRecordsBackupFileNamePrefix
      }
      {
        name: 'Backup__1__BackupType'
        value: 'HenrySaves'
      }
      {
        name: 'Backup__1__AccountType'
        value: 'Personal'
      }
      {
        name: 'Backup__1__ExportFolderId'
        value: henrySavesExportFolderId
      }
      {
        name: 'Backup__1__BackupFolderId'
        value: henrySavesBackupFolderId
      }
      {
        name: 'Backup__1__FileNamePrefix'
        value: henrySavesBackupFileNamePrefix
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
