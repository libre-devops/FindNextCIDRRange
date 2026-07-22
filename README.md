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
rewrite preserves deliberately (existing consumers parse the body). A deployment can opt out: set
the `HONOR_HTTP_STATUS` app setting to `true` (`honor_http_status = true` in tfvars) and errors
travel with the body's `code` as the wire status instead. The bodies are identical in both modes,
so opt in freely on a fresh deployment; leave it off where existing consumers might branch on the
constant 400.

A second endpoint checks any CIDR against Azure's subnet rules using nothing but arithmetic, so
it needs no identity, no Reader grant, and no Azure call at all:

```
GET https://<functionapp>/api/CheckCidr?cidr=10.0.0.0/29
```

```json
{
  "cidr": "10.0.0.0/29",
  "normalized": "10.0.0.0/29",
  "validAzureSubnet": true,
  "prefixLength": 29,
  "totalAddresses": 8,
  "azureReservedAddresses": 5,
  "usableAddresses": 3,
  "reserved": {
    "networkAddress": "10.0.0.0",
    "defaultGateway": "10.0.0.1",
    "azureDns": ["10.0.0.2", "10.0.0.3"],
    "broadcast": "10.0.0.7"
  },
  "firstUsable": "10.0.0.4",
  "lastUsable": "10.0.0.6",
  "reason": null
}
```

Azure reserves five addresses in every subnet (the network address, the default gateway, two for
Azure DNS, and the broadcast address), which is why subnets run from /2 to /29, why a /30 cannot
exist (its 4 addresses cannot fit the reserved 5), and why `GetCidr` has always accepted `cidr` 2
through 29. A /30 therefore answers `validAzureSubnet: false` with the reason spelled out, and
that is a 200: the question was answered. `CheckCidr` postdates the 1.x contract, so it carries
none of the preserved quirks: responses are `application/json` and wire statuses are always
truthful, with 400 reserved for input that cannot be parsed (garbage, a missing prefix length, or
IPv6, where Azure subnets are always /64).

The API documents itself: a deployed app serves a landing page at `/` (a single self-contained
HTML document, no external assets), its OpenAPI description at `/api/openapi.yaml`, and an
interactive Swagger UI at `/api/swagger`. The spec is [`openapi.yaml`](./openapi.yaml) in the
repo root, embedded into the assembly at build time so the two can never drift, and it records
the contract warts and all. Under the hood host.json empties the global route prefix so the root
is routable, and every function pins its full historical `api/...` path, so the wire contract is
unchanged.

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
- The usual estate furniture: a PowerShell justfile, CI that runs the unit tests pinning the wire
  contract (the always-400 wart, the text/plain bodies, the exact error shapes), CodeQL scanning,
  dependabot, and this README.

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

## Security scan exceptions

CI runs a trivy config scan that gates on HIGH and CRITICAL findings. Waivers live in
[`.trivyignore.yaml`](./.trivyignore.yaml), and every waiver is recorded here.

| ID | Where | Justification |
| --- | --- | --- |
| AVD-AZU-0012 | The function's host storage account, inside the composed flex-consumption-function-app module | The host storage must stay reachable: zip deploys arrive from wherever the operator runs `just deploy` (no knowable IP to allow-list) and the Functions platform itself needs the account. Data-plane access is Entra ID via the app's managed identity, the account holds only the function package and host state, and the stack is disposable by design. |
