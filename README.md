<div align="center">
  <a href="https://libredevops.org">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="https://libredevops.org/assets/libre-devops-white.png">
      <img alt="Libre DevOps" src="https://libredevops.org/assets/libre-devops-black.png" width="300">
    </picture>
  </a>
</div>

# FindNextCIDRRange

An HTTP API that answers one question well: what is the next free CIDR of a given size in an Azure
virtual network?

[![CI](https://github.com/libre-devops/FindNextCIDRRange/actions/workflows/ci.yml/badge.svg)](https://github.com/libre-devops/FindNextCIDRRange/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/libre-devops/FindNextCIDRRange)](./LICENSE)

---

> **A word from Gary, the original author**: "This is Gary, the original author of this code. I am
> retiring from Microsoft in July of 2025, and will no longer be doing much in the way of Azure.
> Craig Thacker, who added Terraform support a couple years ago, is taking over ownership. Thanks,
> Craig!"
>
> Thank you, Gary. The original write-up lives on in the
> [Azure networking blog](https://techcommunity.microsoft.com/t5/azure-networking-blog/programmatically-find-next-available-cidr-for-subnet/ba-p/3266016),
> and the code as handed over is preserved in the [`1.0.0`](../../releases/tag/1.0.0) tag.

## Overview

A single-endpoint Azure Function: give it a subscription, resource group, vnet, and a CIDR size,
and it returns the first subnet of that size that fits without overlapping anything already there.

```
GET https://<functionapp>/api/GetCidr?subscriptionId=<sub>&resourceGroupName=<rg>&virtualNetworkName=<vnet>&cidr=<size>[&addressSpace=<cidr>]
```

A successful answer:

```json
{
  "name": "vnet-ldo-uks-dev-cidr-001",
  "id": "/subscriptions/.../virtualNetworks/vnet-ldo-uks-dev-cidr-001",
  "type": "Microsoft.Network/virtualNetworks",
  "location": "uksouth",
  "addressSpace": "10.30.0.0/24",
  "proposedCIDR": "10.30.0.128/26"
}
```

`addressSpace` narrows the search to one of the vnet's address spaces (exact, normalised form);
omitted, the first space that fits wins. `cidr` accepts 2 through 29. Errors return a JSON body of
`{ "code": "...", "message": "..." }`; note the long-standing contract that every error travels
with HTTP 400 on the wire while the meaningful status lives in the body's `code` field, which this
rewrite preserves deliberately (existing consumers parse the body).

## The 2.x rewrite

Version 2.0.0 is the v-next treatment with the contract held byte-stable:

- **.NET 10 isolated worker**, replacing .NET 6 in-process. The original's runtime is EOL and the
  in-process model itself retires in November 2026; .NET 10 is the current LTS, supported on Flex
  Consumption to November 2028. Same route, parameters, bodies, and content type.
- Two inherited bugs fixed without touching the contract: the working status code was a static
  shared across invocations and could bleed one request's error code into another's error body
  (per-request now), and the subscription lookup demanded `subscriptions/read` at subscription
  scope, which made narrowly scoped Reader grants fail with a 403 dressed as a 500 (the resource
  group ID is now constructed directly, so Reader on the vnet's scope is genuinely enough).
- **Terraform on the Libre DevOps registry modules**: a Flex Consumption function app (FC1 plan,
  keyless storage, user-assigned identity, identity-based host storage, all module defaults) and
  Reader grants for the app's identities (that is all the function needs).
- The usual estate furniture: justfile, CI, and this README.

## Deploy

Two paths, chosen entirely by tfvars.

**Standalone** (the default) seeds a small test vnet with two subnets already carved, so the API
has a real question to answer the moment the deploy lands:

```bash
az login
export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
cp terraform/terraform.tfvars.example terraform/terraform.tfvars   # then make it yours (owner tag etc.)
just apply       # the stack: function app, identity, Reader grant, test vnet
just package     # dotnet publish + zip
just deploy      # push the zip to the app
just try         # query the test vnet; expect 10.30.0.128/26
```

**Bring your own network** deploys nothing but the function and points Reader at the networks you
already have: start from `terraform/terraform.tfvars.byon.example` instead, which sets
`deploy_test_vnet = false` and lists your `reader_scopes` (subscription, resource group, or single
vnet IDs; narrower is better). Same `just` steps afterwards.

State is local and gitignored; this is a deploy-into-your-tenant tool, not a shared pipeline.

## Notes

- The function authenticates outward with `DefaultAzureCredential`, so the app's managed identity
  is the credential in Azure, and your az login is the credential when running locally. The stack
  grants Reader to both of the app's identities (system-assigned and user-assigned), because the
  credential chain picks the system-assigned one unless told otherwise.
- Flex Consumption zip deploys flake: `just deploy` pushes with a spaced retry, and the az CLI's
  exit code is not trusted either way (its health check can error even when the deploy landed), so
  the only verdict that counts is `just try` returning an answer.
- The API is anonymous at the function layer by design (the original behaviour); put it behind
  your own network controls or function keys as your environment requires.
