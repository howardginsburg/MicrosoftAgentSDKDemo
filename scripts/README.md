# Grant Cosmos DB RBAC Permissions Script

This script grants the current Azure CLI authenticated user data plane access to an Azure Cosmos DB account using role-based access control (RBAC).

## Prerequisites

- Azure CLI installed and authenticated (`az login`)
- Appropriate permissions to assign roles in the Cosmos DB account
- Bash shell (Git Bash on Windows, or Linux/macOS terminal)

## Usage

### 1. Make the script executable (Linux/macOS)

```bash
chmod +x scripts/grant-cosmos-rbac.sh
```

### 2. Run the script

```bash
./scripts/grant-cosmos-rbac.sh <resource-group-name> <cosmos-account-name>
```

### Example

```bash
./scripts/grant-cosmos-rbac.sh my-resource-group my-cosmos-account
```

### On Windows (Git Bash)

```bash
bash scripts/grant-cosmos-rbac.sh my-resource-group my-cosmos-account
```

## What This Script Does

1. **Gets your Azure CLI user identity** - Retrieves the principal ID of the currently signed-in user
2. **Gets the Cosmos DB account ID** - Retrieves the resource ID to use as the assignment scope
3. **Assigns the built-in role** - Grants "Cosmos DB Built-in Data Contributor" role with these permissions:
   - Read account metadata
   - Read/write containers
   - Read/write items (CRUD operations)

## Permissions Granted

The script uses the built-in **Cosmos DB Built-in Data Contributor** role (`00000000-0000-0000-0000-000000000002`) which includes:

- `Microsoft.DocumentDB/databaseAccounts/readMetadata`
- `Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/*`
- `Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/*`

## Scope

The role is assigned at the **account level**, meaning the user has access to:
- All databases in the account
- All containers in all databases
- All items in all containers

## After Running

1. Wait 1-2 minutes for permissions to propagate
2. Your application can now use `AzureCliCredential` for Cosmos DB authentication
3. The `appsettings.json` only needs the Cosmos DB endpoint (no account key needed)
4. **This script is required** - the application exclusively uses Azure CLI credentials for Cosmos DB

## Troubleshooting

**Error: "Insufficient privileges"**
- You need appropriate permissions to assign roles in the Cosmos DB account
- Contact your Azure administrator to grant you access

**Error: "Resource not found"**
- Verify the resource group name and Cosmos DB account name are correct
- Ensure you're logged into the correct Azure subscription (`az account show`)

**Permissions not working after assignment**
- Wait 2-3 minutes for role assignments to propagate
- Try logging out and back in: `az logout && az login`

## Reference

Based on Microsoft documentation:
https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-connect-role-based-access-control
