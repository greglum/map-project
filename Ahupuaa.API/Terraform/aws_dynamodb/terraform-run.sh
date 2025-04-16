#!/bin/bash
# terraform-run.sh

# Set colors for better readability
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Check if a vars file was provided
if [ "$1" ]; then
  VARS_FILE="$1"
else
  VARS_FILE="terraform.tfvars"
fi

# Check if the vars file exists
if [ ! -f "$VARS_FILE" ]; then
  echo -e "${RED}Error: Variables file '$VARS_FILE' not found!${NC}"
  echo "Usage: ./terraform-run.sh [vars-file.tfvars]"
  exit 1
fi

# Print header
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Terraform Automation Script${NC}"
echo -e "${GREEN}========================================${NC}"
echo -e "${YELLOW}Using variables file: ${NC}$VARS_FILE"
echo ""

# Initialize
echo -e "${YELLOW}Step 1: Initializing Terraform...${NC}"
terraform init
if [ $? -ne 0 ]; then
  echo -e "${RED}Initialization failed!${NC}"
  exit 1
fi
echo ""

# Format code
echo -e "${YELLOW}Step 2: Formatting configuration...${NC}"
terraform fmt
echo ""

# Validate configuration
echo -e "${YELLOW}Step 3: Validating configuration...${NC}"
terraform validate
if [ $? -ne 0 ]; then
  echo -e "${RED}Validation failed!${NC}"
  exit 1
fi
echo ""

# Plan
echo -e "${YELLOW}Step 4: Creating execution plan...${NC}"
terraform plan -var-file="$VARS_FILE" -out=tfplan
if [ $? -ne 0 ]; then
  echo -e "${RED}Plan creation failed!${NC}"
  exit 1
fi
echo ""

# Confirm before applying
echo -e "${YELLOW}Plan created successfully.${NC}"
read -p "Do you want to apply these changes? (y/n): " confirm
if [[ $confirm != [yY] && $confirm != [yY][eE][sS] ]]; then
  echo -e "${YELLOW}Operation cancelled. Your infrastructure remains unchanged.${NC}"
  exit 0
fi

# Apply
echo -e "${YELLOW}Step 5: Applying changes...${NC}"
terraform apply "tfplan"
if [ $? -ne 0 ]; then
  echo -e "${RED}Apply failed!${NC}"
  exit 1
fi

# Success message
echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Terraform deployment complete!${NC}"
echo -e "${GREEN}========================================${NC}"