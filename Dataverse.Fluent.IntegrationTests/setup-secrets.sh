#!/bin/bash
# User Secrets Setup Script for Dataverse.Fluent Integration Tests
# This script helps you quickly configure User Secrets for local development

set -e

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

echo -e "${GREEN}?${NC} ${CYAN}Dataverse.Fluent - User Secrets Setup${NC}"
echo ""

# Check if we're in the correct directory
if [ ! -f "Dataverse.Fluent.IntegrationTests.csproj" ]; then
    echo -e "${RED}?${NC} ${RED}Error: This script must be run from the Dataverse.Fluent.IntegrationTests directory${NC}"
    echo ""
    echo -e "${YELLOW}Current directory: $(pwd)${NC}"
    echo ""
    echo -e "${YELLOW}Please run:${NC}"
    echo -e "  ${CYAN}cd Dataverse.Fluent.IntegrationTests${NC}"
    echo -e "  ${CYAN}./setup-secrets.sh${NC}"
    exit 1
fi

echo -e "${WHITE}This script will help you configure User Secrets for integration tests.${NC}"
echo -e "${WHITE}Your credentials will be stored securely on your local machine only.${NC}"
echo ""

# Prompt for configuration values
echo -e "${YELLOW}Please provide your Dataverse configuration:${NC}"
echo ""

read -p "Environment URL (e.g., https://yourorg.crm9.dynamics.com/): " ENVIRONMENT_URL
read -p "Tenant ID: " TENANT_ID
read -p "Client ID: " CLIENT_ID
read -sp "Client Secret: " CLIENT_SECRET
echo ""

# Validate inputs
if [ -z "$ENVIRONMENT_URL" ] || [ -z "$TENANT_ID" ] || [ -z "$CLIENT_ID" ] || [ -z "$CLIENT_SECRET" ]; then
    echo ""
    echo -e "${RED}?${NC} ${RED}Error: All fields are required${NC}"
    exit 1
fi

echo ""
echo -e "${GREEN}?${NC} ${CYAN}Setting User Secrets...${NC}"
echo ""

# Set User Secrets
dotnet user-secrets set "Dataverse:EnvironmentUrl" "$ENVIRONMENT_URL" > /dev/null
dotnet user-secrets set "Dataverse:ApiVersion" "v9.2" > /dev/null
dotnet user-secrets set "Dataverse:AuthMode" "ClientCredentials" > /dev/null
dotnet user-secrets set "Dataverse:TenantId" "$TENANT_ID" > /dev/null
dotnet user-secrets set "Dataverse:ClientId" "$CLIENT_ID" > /dev/null
dotnet user-secrets set "Dataverse:ClientSecret" "$CLIENT_SECRET" > /dev/null

echo -e "${GREEN}?${NC} ${GREEN}User Secrets configured successfully!${NC}"
echo ""

# Display configured values (without secret)
echo -e "${YELLOW}Configured values:${NC}"
echo -e "  ${WHITE}Environment URL: $ENVIRONMENT_URL${NC}"
echo -e "  ${WHITE}Tenant ID: $TENANT_ID${NC}"
echo -e "  ${WHITE}Client ID: $CLIENT_ID${NC}"
echo -e "  ${WHITE}Client Secret: ********${NC}"
echo -e "  ${WHITE}Auth Mode: ClientCredentials${NC}"
echo -e "  ${WHITE}API Version: v9.2${NC}"
echo ""

# Verify
echo -e "${CYAN}?${NC} ${CYAN}Verifying configuration...${NC}"
echo ""

if dotnet user-secrets list > /dev/null 2>&1; then
    echo -e "${GREEN}?${NC} ${GREEN}User Secrets are properly configured!${NC}"
    echo ""
    echo -e "${YELLOW}You can now run the integration tests:${NC}"
    echo -e "  ${CYAN}dotnet test${NC}"
    echo ""
    echo -e "${YELLOW}To view your secrets at any time, run:${NC}"
    echo -e "  ${CYAN}dotnet user-secrets list${NC}"
    echo ""
else
    echo -e "${RED}?${NC} ${RED}Error verifying User Secrets${NC}"
    exit 1
fi

# Cleanup
unset CLIENT_SECRET
