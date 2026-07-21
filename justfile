# Recipes for FindNextCIDRRange. `just` on its own lists them. Set ARM_SUBSCRIPTION_ID for the
# terraform recipes: export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)

default:
    @just --list

# Restore and build the function (release configuration)
build:
    dotnet build src/Find-NextCidrRange -c Release

# Publish and zip the function ready for deployment
package:
    dotnet publish src/Find-NextCidrRange -c Release -o publish
    cd publish && zip -qr ../function.zip .
    @echo "function.zip ready"

# Deploy the packaged function to the app the stack created. Pushed with a spaced retry because
# flex zip deploys flake transiently, and the CLI's exit code is untrustworthy either way (its
# keyless host-key health check can error even on landed deploys), so success is only ever judged
# by `just try`, never by this recipe's exit code.
deploy:
    #!/usr/bin/env bash
    set -uo pipefail
    rg="$(terraform -chdir=terraform output -raw resource_group_name)"
    app="$(terraform -chdir=terraform output -raw function_app_name)"
    for attempt in 1 2 3; do
        az functionapp deployment source config-zip -g "$rg" -n "$app" --src function.zip && break
        [ "$attempt" = "3" ] || { echo "retrying in 20s"; sleep 20; }
    done
    echo "pushed; judge success with: just try"

# Terraform lifecycle against the stack
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
