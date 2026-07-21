data "azurerm_subscription" "current" {}

output "example_query" {
  description = "A ready-made query against the test vnet; with its two /26s used, the answer should propose 10.30.0.128/26."
  value       = var.deploy_test_vnet ? "https://${module.flex_function_app.default_hostnames[local.func_name]}/api/GetCidr?subscriptionId=${data.azurerm_subscription.current.subscription_id}&resourceGroupName=${local.rg_name}&virtualNetworkName=${local.vnet_name}&cidr=26" : "test vnet disabled"
}

output "function_app_name" {
  description = "The function app name, for func or az deployments."
  value       = local.func_name
}

output "resource_group_name" {
  description = "The resource group holding the stack."
  value       = local.rg_name
}

output "function_hostname" {
  description = "The function app's default hostname."
  value       = module.flex_function_app.default_hostnames[local.func_name]
}
