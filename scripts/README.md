# Grant Azure RBAC Permissions Scripts

These scripts grant the current Azure CLI authenticated user RBAC permissions to Azure resources using role-based access control.

## Scripts

### 1. grant-cosmos-rbac.sh
Grants Cosmos DB data plane access using the built-in "Cosmos DB Built-in Data Contributor" role.

### 2. grant-foundry-rbac.sh
Grants Azure AI Foundry access using the built-in "Cognitive Services OpenAI User" role.

## Prerequisites

- Azure CLI installed and authenticated (`az login`)
- Appropriate permissions to assign roles in the Azure resources
- Bash shell (Git Bash on Windows, or Linux/macOS terminal)

## Usage

### Grant Cosmos DB Permissions

#### 1. Make the script executable (Linux/macOS)

```bash
chmod +x scripts/grant-cosmos-rbac.sh
```

#### 2. Run the script

```bash
./scripts/grant-cosmos-rbac.sh <resource-group-name> <cosmos-account-name>
```

#### Example

```bash
./scripts/grant-cosmos-rbac.sh my-resource-group my-cosmos-account
```

#### On Windows (Git Bash)

```bash
bash scripts/grant-cosmos-rbac.sh my-resource-group my-cosmos-account
```

### Grant Azure AI Foundry Permissions

#### 1. Make the script executable (Linux/macOS)

```bash
chmod +x scripts/grant-foundry-rbac.sh
```

#### 2. Run the script

```bash
./scripts/grant-foundry-rbac.sh <resource-group-name> <foundry-resource-name>
```

#### Example

```bash
./scripts/grant-foundry-rbac.sh my-resource-group my-ai-resource
```

#### On Windows (Git Bash)

```bash
bash scripts/grant-foundry-rbac.sh my-resource-group my-ai-resource
```

## What These Scripts Do

### grant-cosmos-rbac.sh

1. **Gets your Azure CLI user identity** - Retrieves the principal ID of the currently signed-in user
2. **Gets the Cosmos DB account ID** - Retrieves the resource ID to use as the assignment scope
3. **Assigns the built-in role** - Grants "Cosmos DB Built-in Data Contributor" role with these permissions:
   - Read account metadata
   - Read/write containers
   - Read/write items (CRUD operations)

### grant-foundry-rbac.sh

1. **Gets your Azure CLI user identity** - Retrieves the principal ID of the currently signed-in user
2. **Gets the Azure AI Foundry resource ID** - Retrieves the resource ID to use as the assignment scope
3. **Assigns the built-in role** - Grants "Cognitive Services OpenAI User" role with these permissions:
   - Read deployment information
   - Call AI APIs (chat, completions, embeddings, etc.)
   - Use deployed models

## Permissions Granted

### Cosmos DB Built-in Data Contributor

The script uses the built-in **Cosmos DB Built-in Data Contributor** role (`00000000-0000-0000-0000-000000000002`) which includes:

- `Microsoft.DocumentDB/databaseAccounts/readMetadata`
- `Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/*`
- `Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/*`

### Cognitive Services OpenAI User

The script uses the built-in **Cognitive Services OpenAI User** role which allows:
- Access to view keys and deployments
- Call inference APIs (chat completions, embeddings, etc.)
- Use all deployed models
- Does NOT allow model management or key regeneration

## Scope

### Cosmos DB
The role is assigned at the **account level**, meaning the user has access to:
- All databases in the account
- All containers in all databases
- All items in all containers

### Azure AI Foundry
The role is assigned at the **resource level**, meaning the user has access to:
- All deployments in the resource
- All inference operations

## After Running

### After grant-cosmos-rbac.sh
1. Wait 1-2 minutes for permissions to propagate
2. Your application can now use `AzureCliCredential` for Cosmos DB authentication
3. The `appsettings.json` only needs the Cosmos DB endpoint (no account key needed)
4. **This script is required** - the application exclusively uses Azure CLI credentials for Cosmos DB

### After grant-foundry-rbac.sh
1. Wait 1-2 minutes for permissions to propagate
2. Your application can now use `AzureCliCredential` for Azure AI Foundry authentication
3. The `appsettings.json` only needs the Azure AI Foundry endpoint (no API key needed)
4. **This script is required** - the application exclusively uses Azure CLI credentials for Azure AI Foundry

## Troubleshooting

**Error: "Insufficient privileges"**
- You need appropriate permissions to assign roles in the Azure resource
- Contact your Azure administrator to grant you access

**Error: "Resource not found"**
- Verify the resource group name and resource name are correct
- Ensure you're logged into the correct Azure subscription (`az account show`)

**Permissions not working after assignment**
- Wait 2-3 minutes for role assignments to propagate
- Try logging out and back in: `az logout && az login`

## References

- [Azure Cosmos DB RBAC](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-connect-role-based-access-control)
- [Azure OpenAI RBAC](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/role-based-access-control)
