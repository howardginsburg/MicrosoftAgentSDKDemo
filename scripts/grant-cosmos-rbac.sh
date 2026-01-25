#!/bin/bash
# Grant Cosmos DB RBAC permissions to the current Azure CLI user
# Based on: https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-connect-role-based-access-control

set -e

# Configuration
RESOURCE_GROUP_NAME="${1}"
COSMOS_ACCOUNT_NAME="${2}"

if [ -z "$RESOURCE_GROUP_NAME" ] || [ -z "$COSMOS_ACCOUNT_NAME" ]; then
    echo "Usage: ./grant-cosmos-rbac.sh <resource-group-name> <cosmos-account-name>"
    echo ""
    echo "Example:"
    echo "  ./grant-cosmos-rbac.sh my-resource-group my-cosmos-account"
    exit 1
fi

echo "========================================="
echo "Granting Cosmos DB RBAC Permissions"
echo "========================================="
echo "Resource Group: $RESOURCE_GROUP_NAME"
echo "Cosmos Account: $COSMOS_ACCOUNT_NAME"
echo ""

# Get the current signed-in user's principal ID
echo "Getting current user's principal ID..."
PRINCIPAL_ID=$(az ad signed-in-user show --query id -o tsv)
echo "Principal ID: $PRINCIPAL_ID"
echo ""

# Get the Cosmos DB account resource ID (used as scope)
echo "Getting Cosmos DB account resource ID..."
ACCOUNT_ID=$(az cosmosdb show \
    --resource-group "$RESOURCE_GROUP_NAME" \
    --name "$COSMOS_ACCOUNT_NAME" \
    --query id -o tsv)
echo "Account ID: $ACCOUNT_ID"
echo ""

# Get the built-in role definition ID by name
echo "Looking up role definition: Cosmos DB Built-in Data Contributor..."
ROLE_DEFINITION_ID=$(az cosmosdb sql role definition list \
    --resource-group "$RESOURCE_GROUP_NAME" \
    --account-name "$COSMOS_ACCOUNT_NAME" \
    --query "[?roleName=='Cosmos DB Built-in Data Contributor'].id | [0]" -o tsv)

if [ -z "$ROLE_DEFINITION_ID" ]; then
    echo "❌ Error: Could not find 'Cosmos DB Built-in Data Contributor' role definition"
    echo "This role should exist by default in Cosmos DB accounts."
    exit 1
fi

echo "Role Definition ID: $ROLE_DEFINITION_ID"
echo ""

# Create the role assignment
echo "Creating role assignment..."
az cosmosdb sql role assignment create \
    --resource-group "$RESOURCE_GROUP_NAME" \
    --account-name "$COSMOS_ACCOUNT_NAME" \
    --role-definition-id "$ROLE_DEFINITION_ID" \
    --principal-id "$PRINCIPAL_ID" \
    --scope "$ACCOUNT_ID"

echo ""
echo "========================================="
echo "✅ Role assignment completed successfully!"
echo "========================================="
echo ""
echo "The current Azure CLI user now has data plane access to:"
echo "  Account: $COSMOS_ACCOUNT_NAME"
echo "  Scope: All databases and containers"
echo ""
echo "Permissions granted:"
echo "  - Read account metadata"
echo "  - Read/write containers"
echo "  - Read/write items"
echo ""
echo "Note: It may take a few minutes for permissions to propagate."
