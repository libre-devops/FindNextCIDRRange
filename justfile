# Recipes for FindNextCIDRRange, run in PowerShell (the estate convention, and dotnet is first
# class there). `just` on its own lists them. Set ARM_SUBSCRIPTION_ID for the terraform recipes:
#   export ARM_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
# The usual flow: just init, apply, package, deploy, then smoke (or try) to judge the deploy.

set shell := ["pwsh", "-NoProfile", "-Command"]

# Shebang recipes execute from a temp file, and just's default location (XDG_RUNTIME_DIR) is
# mounted noexec on some systems (WSL2 among them), which fails every such recipe with
# "Permission denied". /tmp is executable close to everywhere.
set tempdir := '/tmp'

default:
    @just --list

# Restore and build the function and the tests (release configuration)
build:
    dotnet build FindNextCIDRRange.sln -c Release

# Run the unit tests that guard the wire contract
test:
    dotnet test FindNextCIDRRange.sln -c Release

# Publish and zip the function ready for deployment. The stale zip is removed first: zip updates
# an existing archive in place, so deleted project files would otherwise linger in the package.
package:
    if (Test-Path function.zip) { Remove-Item function.zip }
    dotnet publish src/Find-NextCidrRange -c Release -o publish
    cd publish && zip -qr ../function.zip .
    @echo "function.zip ready"

# Deploy the packaged function to the app the stack created. Pushed with a spaced retry because
# flex zip deploys flake transiently, and the CLI's exit code is untrustworthy either way (its
# keyless host-key health check can error even on landed deploys), so success is only ever judged
# by `just smoke` or `just try`, never by this recipe's exit code.
deploy:
    #!/usr/bin/env pwsh
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'
    $rg = terraform -chdir=terraform output -raw resource_group_name
    $app = terraform -chdir=terraform output -raw function_app_name
    # terraform output exits 0 with empty stdout when the state holds no outputs at all (a
    # destroyed stack), so emptiness is the reliable "no stack" signal, not the exit code.
    if ($LASTEXITCODE -ne 0 -or -not $rg -or -not $app) { throw 'no deployed stack in the terraform outputs; run: just apply' }
    foreach ($attempt in 1, 2, 3) {
        az functionapp deployment source config-zip -g $rg -n $app --src function.zip
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -lt 3) { Write-Host 'retrying in 20s'; Start-Sleep -Seconds 20 }
    }
    Write-Host 'pushed; judge success with: just smoke'

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

# Trivy config scan over the repo, gating on HIGH and CRITICAL like CI. Waivers live in
# .trivyignore.yaml, and every waiver has a row in the README's security scan exceptions table.
# Run after init so the composed modules under .terraform/modules are scanned too.
scan:
    trivy config --ignorefile .trivyignore.yaml --severity CRITICAL,HIGH --exit-code 1 .

# Everything CI runs: build, unit tests, then terraform fmt, validate, and the trivy gate
check:
    dotnet build FindNextCIDRRange.sln -c Release
    dotnet test FindNextCIDRRange.sln -c Release --no-build
    terraform -chdir=terraform fmt -check -recursive
    terraform -chdir=terraform init -backend=false -input=false > $null
    terraform -chdir=terraform validate
    trivy config --ignorefile .trivyignore.yaml --severity CRITICAL,HIGH --exit-code 1 .

# Call the deployed API against the test vnet (expects the next free /26)
try:
    curl -s "$(terraform -chdir=terraform output -raw example_query)"

# Exercise every live endpoint of the deployed app: the landing page, Swagger UI, the OpenAPI
# spec, the zero-access CIDR checker (both verdicts), the happy path against the test vnet
# (skipped on the bring-your-own-network path), and the preserved error contract (HTTP 400 on
# the wire, the meaningful code in the body)
smoke:
    #!/usr/bin/env pwsh
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'
    $fqdn = terraform -chdir=terraform output -raw function_hostname
    # Empty with exit 0 when the state holds no outputs (a destroyed stack); see deploy.
    if ($LASTEXITCODE -ne 0 -or -not $fqdn) { throw 'no deployed stack in the terraform outputs; run: just apply' }
    foreach ($path in '/', '/api/swagger', '/api/openapi.yaml') {
        $page = Invoke-WebRequest -Uri "https://$fqdn$path" -TimeoutSec 30
        Write-Host "GET $path -> $($page.StatusCode)"
    }
    $ok = (Invoke-WebRequest -Uri "https://$fqdn/api/CheckCidr?cidr=10.0.0.0/29" -TimeoutSec 30).Content | ConvertFrom-Json
    if (-not $ok.validAzureSubnet -or $ok.usableAddresses -ne 3) { throw 'a /29 must be valid in Azure with 3 usable addresses' }
    $no = (Invoke-WebRequest -Uri "https://$fqdn/api/CheckCidr?cidr=10.0.0.0/30" -TimeoutSec 30).Content | ConvertFrom-Json
    if ($no.validAzureSubnet) { throw 'a /30 must not be a valid Azure subnet' }
    Write-Host 'GET /api/CheckCidr (/29 yes, /30 no) -> azure subnet rules answering'
    $url = terraform -chdir=terraform output -raw example_query
    if ($url -ne 'test vnet disabled') {
        $answer = (Invoke-WebRequest -Uri $url -TimeoutSec 30).Content | ConvertFrom-Json
        if (-not $answer.proposedCIDR) { throw 'happy path answered without a proposedCIDR' }
        Write-Host "GET /api/GetCidr (test vnet) -> $($answer.proposedCIDR)"
    } else {
        Write-Host 'test vnet disabled; skipping the happy path'
    }
    $bad = Invoke-WebRequest -Uri "https://$fqdn/api/GetCidr?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=55" -TimeoutSec 30 -SkipHttpErrorCheck
    if ([int]$bad.StatusCode -ne 400) { throw "expected 400 on the wire, got $([int]$bad.StatusCode)" }
    if ($bad.Content -notmatch '"code": "400"') { throw 'the error body lost its code field' }
    Write-Host 'GET /api/GetCidr (bad cidr) -> error contract intact'
    Write-Host 'smoke: all endpoints answering'
