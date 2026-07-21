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

# Deploy the packaged function to the app the stack created
deploy:
    az functionapp deployment source config-zip -g "$(terraform -chdir=terraform output -raw resource_group_name)" -n "$(terraform -chdir=terraform output -raw function_app_name)" --src function.zip

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
