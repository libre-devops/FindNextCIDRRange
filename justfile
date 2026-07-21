# Recipes for FindNextCIDRRange. `just` on its own lists them. Set ARM_SUBSCRIPTION_ID for the
# terraform recipes: export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
# The usual flow: just init, apply, package, deploy, then smoke (or try) to judge the deploy.

# Shebang recipes execute from a temp file, and just's default location (XDG_RUNTIME_DIR) is
# mounted noexec on some systems (WSL2 among them), which fails every such recipe with
# "Permission denied". /tmp is executable close to everywhere.
set tempdir := '/tmp'

default:
    @just --list

# Restore and build the function (release configuration)
build:
    dotnet build src/Find-NextCidrRange -c Release

# Publish and zip the function ready for deployment. The stale zip is removed first: zip updates
# an existing archive in place, so deleted project files would otherwise linger in the package.
package:
    rm -f function.zip
    dotnet publish src/Find-NextCidrRange -c Release -o publish
    cd publish && zip -qr ../function.zip .
    @echo "function.zip ready"

# Deploy the packaged function to the app the stack created. Pushed with a spaced retry because
# flex zip deploys flake transiently, and the CLI's exit code is untrustworthy either way (its
# keyless host-key health check can error even on landed deploys), so success is only ever judged
# by `just smoke` or `just try`, never by this recipe's exit code.
deploy:
    #!/usr/bin/env bash
    set -uo pipefail
    rg="$(terraform -chdir=terraform output -raw resource_group_name)"
    app="$(terraform -chdir=terraform output -raw function_app_name)"
    for attempt in 1 2 3; do
        az functionapp deployment source config-zip -g "$rg" -n "$app" --src function.zip && break
        [ "$attempt" = "3" ] || { echo "retrying in 20s"; sleep 20; }
    done
    echo "pushed; judge success with: just smoke"

# Package and deploy in one go
ship: package deploy

# Terraform lifecycle against the stack
init *args:
    terraform -chdir=terraform init {{ args }}

plan *args:
    terraform -chdir=terraform plan {{ args }}

apply *args:
    terraform -chdir=terraform apply -auto-approve {{ args }}

destroy *args:
    terraform -chdir=terraform destroy -auto-approve {{ args }}

# Everything CI runs: build plus terraform fmt and validate
check:
    dotnet build src/Find-NextCidrRange -c Release
    terraform -chdir=terraform fmt -check -recursive
    terraform -chdir=terraform init -backend=false -input=false > /dev/null
    terraform -chdir=terraform validate

# Call the deployed API against the test vnet (expects the next free /26)
try:
    curl -s "$(terraform -chdir=terraform output -raw example_query)"

# Exercise every live endpoint of the deployed app: the landing page, Swagger UI, the OpenAPI
# spec, the happy path against the test vnet (skipped on the bring-your-own-network path), and
# the preserved error contract (HTTP 400 on the wire, the meaningful code in the body)
smoke:
    #!/usr/bin/env bash
    set -euo pipefail
    host="$(terraform -chdir=terraform output -raw function_hostname)"
    for path in "/" "/api/swagger" "/api/openapi.yaml"; do
        code=$(curl -s -o /dev/null -w '%{http_code}' -m 30 "https://$host$path")
        echo "GET $path -> $code"
        [ "$code" = "200" ]
    done
    url="$(terraform -chdir=terraform output -raw example_query)"
    if [ "$url" != "test vnet disabled" ]; then
        curl -s -m 30 "$url" | grep -q proposedCIDR && echo "GET /api/GetCidr (test vnet) -> proposedCIDR returned"
    else
        echo "test vnet disabled; skipping the happy path"
    fi
    curl -s -m 30 "https://$host/api/GetCidr?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=55" | grep -q '"code": "400"' && echo "GET /api/GetCidr (bad cidr) -> error contract intact"
    echo "smoke: all endpoints answering"
