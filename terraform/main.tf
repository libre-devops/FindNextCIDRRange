# The v-next infrastructure for FindNextCIDRRange: the same function, hosted properly. A Flex
# Consumption function app on .NET 10 isolated (the in-process model the original used retires in
# November 2026), from the estate's flex-consumption-function-app module: dedicated FC1 plan,
# keyless storage with a deployment container, a user-assigned identity with identity-based host
# storage, all by module default. Blocks are ordered by dependency, top to bottom.
#
# Two deployment paths, chosen by tfvars alone:
#   Standalone (default): deploy_test_vnet = true seeds a small vnet with two subnets carved, so
#   the API has a real question to answer the moment the deploy lands. The demo of how it works.
#   Bring your own network: deploy_test_vnet = false, reader_scopes = the subscription, resource
#   group, or vnet IDs holding the networks you will query. Nothing but the function deploys, and
#   Reader lands exactly where you point it.
locals {
  location  = lookup(var.regions, var.loc, "uksouth")
  rg_name   = "rg-${var.short}-${var.loc}-${var.env}-cidr-001"
  func_name = "func-${var.short}-${var.loc}-${var.env}-cidr-001"
  vnet_name = "vnet-${var.short}-${var.loc}-${var.env}-cidr-001"

  # Both identities need the grant: the app carries a user-assigned identity for host storage, but
  # DefaultAzureCredential with no AZURE_CLIENT_ID authenticates as the system-assigned one, so
  # granting only one of them leaves the function 403ing depending on the credential chain.
  function_principal_ids = distinct(compact([
    module.flex_function_app.identity_principal_ids[local.func_name].system_assigned,
    module.flex_function_app.identity_principal_ids[local.func_name].user_assigned,
  ]))
}

module "tags" {
  source  = "libre-devops/tags/azurerm"
  version = "~> 4.0"

  cost_centre     = var.cost_centre
  owner           = var.owner
  additional_tags = { Application = "FindNextCIDRRange" }
}

module "rg" {
  source  = "libre-devops/rg/azurerm"
  version = "~> 4.0"

  resource_groups = [{ name = local.rg_name, location = local.location, tags = module.tags.tags }]
}

module "flex_function_app" {
  source  = "libre-devops/flex-consumption-function-app/azurerm"
  version = "~> 4.0"

  resource_group_id = module.rg.ids[local.rg_name]
  location          = local.location
  tags              = module.tags.tags

  function_apps = {
    (local.func_name) = {
      runtime_name    = "dotnet-isolated"
      runtime_version = "10.0"
    }
  }
}

# Reader is exactly what the function needs to enumerate a vnet's subnets, nothing more. The
# standalone path grants it on this resource group for the test vnet below; the bring-your-own
# path grants it on each scope in var.reader_scopes. Keys index into the caller's literal list,
# so they stay plan-known.
module "role_assignment" {
  source  = "libre-devops/role-assignment/azurerm"
  version = "~> 4.1"

  role_assignments = merge(
    var.deploy_test_vnet ? {
      test-vnet-reader = {
        scope                            = module.rg.ids[local.rg_name]
        principal_ids                    = local.function_principal_ids
        role_names                       = ["Reader"]
        principal_type                   = "ServicePrincipal"
        skip_service_principal_aad_check = true
      }
    } : {},
    {
      for idx, scope in var.reader_scopes : "byon-reader-${idx}" => {
        scope                            = scope
        principal_ids                    = local.function_principal_ids
        role_names                       = ["Reader"]
        principal_type                   = "ServicePrincipal"
        skip_service_principal_aad_check = true
      }
    }
  )
}

# A small test vnet with two subnets already carved, so the API has a real question to answer:
# 10.30.0.0/24 with two /26s used means the next free /26 is 10.30.0.128/26.
module "subnet_calculator" {
  count = var.deploy_test_vnet ? 1 : 0

  source  = "libre-devops/subnet-calculator/azurerm"
  version = "~> 4.0"

  base_cidr = "10.30.0.0/24"
  vnet_name = local.vnet_name

  subnets = [
    { purpose = "app", size = 26 },
    { purpose = "data", size = 26 },
  ]
}

module "network" {
  count = var.deploy_test_vnet ? 1 : 0

  source  = "libre-devops/network/azurerm"
  version = "~> 4.0"

  resource_group_id = module.rg.ids[local.rg_name]
  location          = local.location
  tags              = module.tags.tags

  vnet_name     = local.vnet_name
  address_space = [module.subnet_calculator[0].base_cidr]
  subnets = {
    for name, subnet in module.subnet_calculator[0].network_subnets : name => {
      address_prefixes = subnet.address_prefixes
    }
  }
}
