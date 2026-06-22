@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix for resource names.')
param namePrefix string = 'aiops'

@description('Container image for the API app. Defaults to a placeholder until CI pushes the real image.')
param apiImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the web app. Defaults to a placeholder until CI pushes the real image.')
param webImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Region for Azure OpenAI (must support the embedding model; not available in centralindia).')
param openAiLocation string = 'eastus2'

var token = uniqueString(resourceGroup().id)
var acrName = '${namePrefix}acr${token}'
var storageName = '${namePrefix}st${token}'
var apiAppName = 'ai-ops-api'
var webAppName = 'ai-ops-web'
var redisAppName = 'ai-ops-redis'
var searchName = '${namePrefix}-search-${token}'
var openAiName = '${namePrefix}-openai-${token}'
var embeddingDeployment = 'text-embedding-3-small'
var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var openAiUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
var searchServiceContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0')
var searchIndexDataContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-log'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-appi'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id'
  location: location
}

// Allow the managed identity to pull images from ACR.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, uami.id, acrPullRoleId)
  scope: acr
  properties: {
    roleDefinitionId: acrPullRoleId
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Azure AI Search: the durable, shared vector index (ADR-001). RBAC + key auth; the API uses its
// managed identity (no keys in config).
resource search 'Microsoft.Search/searchServices@2024-03-01-preview' = {
  name: searchName
  location: location
  sku: { name: 'free' }
  properties: {
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http403'
      }
    }
    replicaCount: 1
    partitionCount: 1
  }
}

// Azure OpenAI: real embeddings (text-embedding-3-small). Placed in a region that supports the SKU.
resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: openAiLocation
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'
  }
}

resource embedding 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: embeddingDeployment
  sku: { name: 'Standard', capacity: 50 }
  properties: {
    model: {
      format: 'OpenAI'
      name: embeddingDeployment
      version: '1'
    }
  }
}

// Grant the API's managed identity passwordless access to OpenAI and Search (data + index mgmt).
resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, uami.id, openAiUserRoleId)
  scope: openAi
  properties: {
    roleDefinitionId: openAiUserRoleId
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource searchContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, uami.id, searchServiceContributorRoleId)
  scope: search
  properties: {
    roleDefinitionId: searchServiceContributorRoleId
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource searchDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, uami.id, searchIndexDataContributorRoleId)
  scope: search
  properties: {
    roleDefinitionId: searchIndexDataContributorRoleId
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

var storageConnString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

// Self-hosted Redis on Container Apps: a single internal (TCP, not internet-exposed) replica that
// acts as the shared store for the semantic cache, rate limiter, and token budget. This keeps cost
// near zero and stays in-environment; the managed alternative is Azure Managed Redis. No password is
// set because the endpoint is only reachable from inside the Container Apps environment.
resource redisApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: redisAppName
  location: location
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 6379
        exposedPort: 6379
        transport: 'tcp'
      }
    }
    template: {
      containers: [
        {
          name: 'redis'
          image: 'redis:7-alpine'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

var redisConnString = '${redisAppName}:6379,abortConnect=False'

// API: Azure Functions container, internal ingress only (not exposed to the internet).
resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${uami.id}': {} }
  }
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 80
        transport: 'auto'
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: uami.id
        }
      ]
      secrets: [
        {
          name: 'azurewebjobsstorage'
          value: storageConnString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AzureWebJobsStorage', secretRef: 'azurewebjobsstorage' }
            { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
            { name: 'AI_PROVIDER', value: 'local' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
            { name: 'RATE_LIMIT_PER_MINUTE', value: '20' }
            { name: 'DAILY_TOKEN_BUDGET', value: '100000' }
            { name: 'CACHE_SIMILARITY_THRESHOLD', value: '0.95' }
            { name: 'STATE_STORE', value: 'redis' }
            { name: 'REDIS_CONNECTION', value: redisConnString }
            { name: 'VECTOR_STORE', value: 'azuresearch' }
            { name: 'SEARCH_ENDPOINT', value: 'https://${search.name}.search.windows.net' }
            { name: 'SEARCH_INDEX', value: 'runbooks' }
            { name: 'EMBEDDING_PROVIDER', value: 'azureopenai' }
            { name: 'AZURE_OPENAI_ENDPOINT', value: openAi.properties.endpoint }
            { name: 'AZURE_OPENAI_EMBEDDING_DEPLOYMENT', value: embeddingDeployment }
            { name: 'AZURE_CLIENT_ID', value: uami.properties.clientId }
          ]
        }
      ]
      scale: {
        // State now lives in Redis (shared across replicas), so the API can safely scale out:
        // rate limits and budgets stay globally consistent regardless of which replica responds.
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
  dependsOn: [
    acrPull
    openAiUser
    searchContributor
    searchDataContributor
    embedding
  ]
}

// Web: Next.js container, public ingress. Proxies to the API over the internal network.
resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${uami.id}': {} }
  }
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: uami.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'API_BASE_URL', value: 'https://${apiApp.properties.configuration.ingress.fqdn}' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
  dependsOn: [
    acrPull
  ]
}

output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output apiAppName string = apiApp.name
output webAppName string = webApp.name
output apiInternalFqdn string = apiApp.properties.configuration.ingress.fqdn
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output redisAppName string = redisApp.name
output searchName string = search.name
output openAiName string = openAi.name
