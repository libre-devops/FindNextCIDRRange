# The v-next infrastructure for FindNextCIDRRange: the same function, hosted properly. A Flex
# Consumption function app on .NET 8 isolated (the in-process model the original used retires in
# November 2026), from the estate's flex-consumption-function-app module: dedicated FC1 plan,
# keyless storage with a deployment container, a user-assigned identity with identity-based host
# storage, all by module default. The identity
# gets Reader on the resource group, which is what the function needs to read vnets and subnets;
# grant it Reader on whatever scopes hold the vnets you query in real use. Blocks are ordered by
# dependency, top to bottom.
locals {
  location  = lookup(var.regions, var.loc, "uksouth")
  rg_name   = "rg-${var.short}-${var.loc}-${var.env}-cidr-001"
  func_name = "func-${var.short}-${var.loc}-${var.env}-cidr-001"
  vnet_name = "vnet-${var.short}-${var.loc}-${var.env}-cidr-001"
}

module "tags" {
  source  = "libre-devops/tags/azurerm"
  version = "~> 4.0"

  cost_centre     = "1888/67"
  owner           = "platform@example.com"
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
      runtime_version = "8.0"
    }
  }
}

# Reader is exactly what the function needs to enumerate a vnet's subnets; scoped to this resource
# group for the test vnet below. Real deployments repeat this grant at whatever scope their vnets
# live in.
module "role_assignment" {
  source  = "libre-devops/role-assignment/azurerm"
  version = "~> 4.1"

  role_assignments = {
    vnet-reader = {
      scope                            = module.rg.ids[local.rg_name]
      principal_ids                    = [coalesce(module.flex_function_app.identity_principal_ids[local.func_name].user_assigned, module.flex_function_app.identity_principal_ids[local.func_name].system_assigned)]
      role_names                       = ["Reader"]
      principal_type                   = "ServicePrincipal"
      skip_service_principal_aad_check = true
    }
  }
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
