#!/bin/bash
# Grant Azure AI Foundry RBAC permissions to the current Azure CLI user
# Based on: https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/role-based-access-control

set -e

# Configuration
RESOURCE_GROUP_NAME="${1}"
FOUNDRY_RESOURCE_NAME="${2}"

if [ -z "$RESOURCE_GROUP_NAME" ] || [ -z "$FOUNDRY_RESOURCE_NAME" ]; then
    echo "Usage: ./grant-foundry-rbac.sh <resource-group-name> <foundry-resource-name>"
    echo ""
    echo "Example:"
    echo "  ./grant-foundry-rbac.sh my-resource-group my-ai-resource"
    exit 1
fi

echo "========================================="
echo "Granting Azure AI Foundry RBAC Permissions"
echo "========================================="
echo "Resource Group: $RESOURCE_GROUP_NAME"
echo "Foundry Resource: $FOUNDRY_RESOURCE_NAME"
echo ""

# Get the current signed-in user's principal ID
echo "Getting current user's principal ID..."
PRINCIPAL_ID=$(az ad signed-in-user show --query id -o tsv)
echo "Principal ID: $PRINCIPAL_ID"
echo ""

# Get the Azure AI Foundry resource ID (used as scope)
echo "Getting Azure AI Foundry resource ID..."
RESOURCE_ID=$(az cognitiveservices account show \
    --resource-group "$RESOURCE_GROUP_NAME" \
    --name "$FOUNDRY_RESOURCE_NAME" \
    --query id -o tsv)
echo "Resource ID: $RESOURCE_ID"
echo ""

# Role name for Azure AI Foundry access
ROLE_NAME="Cognitive Services OpenAI User"
echo "Assigning role: $ROLE_NAME"
echo ""

# Create the role assignment
echo "Creating role assignment..."
az role assignment create \
    --assignee "$PRINCIPAL_ID" \
    --role "$ROLE_NAME" \
    --scope "$RESOURCE_ID"

echo ""
echo "========================================="
echo "✅ Role assignment completed successfully!"
echo "========================================="
echo ""
echo "The current Azure CLI user now has access to:"
echo "  Resource: $FOUNDRY_RESOURCE_NAME"
echo "  Scope: AI model deployments and operations"
echo ""
echo "Permissions granted:"
echo "  - Read deployment information"
echo "  - Call AI APIs (chat, completions, embeddings, etc.)"
echo "  - Use deployed models"
echo ""
echo "Note: It may take a few minutes for permissions to propagate."
