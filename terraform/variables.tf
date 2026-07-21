variable "cost_centre" {
  description = "Cost centre tag applied to everything. Replace the placeholder with yours."
  type        = string
  default     = "1888/67"
}

variable "deploy_test_vnet" {
  description = "Deploy a small vnet with pre-seeded subnets alongside the function, so the API has something real to answer against. Costs nothing; turn off for a production deployment."
  type        = bool
  default     = true
}

variable "env" {
  description = "Environment code used in resource names."
  type        = string
  default     = "dev"
}

variable "loc" {
  description = "Outfix: short Azure region code used in resource names."
  type        = string
  default     = "uks"
}

variable "owner" {
  description = "Owner tag applied to everything: the person or team accountable for the deployment. Replace the placeholder with yours."
  type        = string
  default     = "craig@craigthacker.dev"
}

variable "regions" {
  description = "Map of short region codes to Azure region slugs."
  type        = map(string)
  default = {
    uks = "uksouth"
    ukw = "ukwest"
    eus = "eastus"
    euw = "westeurope"
  }
}

variable "short" {
  description = "Infix: short product code used in resource names."
  type        = string
  default     = "ldo"
}
