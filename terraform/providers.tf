# Personal-tenant deployment: Azure CLI user auth and local state. Set ARM_SUBSCRIPTION_ID before
# running (azurerm 4.x requires it): export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
provider "azurerm" {
  features {}
}
