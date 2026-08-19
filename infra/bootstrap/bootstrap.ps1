<#
.SYNOPSIS
    Creates the Azure resources Terraform needs before Terraform can manage anything.

.DESCRIPTION
    Terraform's azurerm backend needs a storage account to hold state, but that storage
    account cannot itself be created by the Terraform that uses it. This script resolves
    that chicken-and-egg, and nothing else. Everything it creates is deliberately outside
    Terraform's control.

    The same reasoning applies to the CI/CD role assignment: GitHub Actions needs
    Contributor on the subscription in order to run `terraform apply`, so that grant
    cannot be something `terraform apply` creates. Note the split from the build plan's
    original sketch: the CI/CD *app registration* is Terraform-managed in
    infra/terraform/identity, while only its *role assignment* lives here.

    Idempotent. Safe to run repeatedly; existing resources are left alone.

.EXAMPLE
    ./bootstrap.ps1

.EXAMPLE
    ./bootstrap.ps1 -CicdClientId 00000000-0000-0000-0000-000000000000
#>
[CmdletBinding()]
param(
    [string] $Location = 'eastus2',

    [string] $ResourceGroupName = 'rg-codejudge-tfstate',

    # Storage account names are globally unique across all of Azure, 3 to 24 characters,
    # lowercase alphanumeric only. Left empty, a deterministic name is derived from the
    # subscription id so repeated runs agree without needing the value written down.
    [string] $StorageAccountName = '',

    [string] $ContainerName = 'tfstate',

    # Client id of the CI/CD app registration created by infra/terraform/identity.
    # Supply it to grant Contributor on the subscription. Omit to skip that step.
    [string] $CicdClientId = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Skip { param([string] $Message) Write-Host "    $Message" -ForegroundColor DarkGray }

# --- Preconditions ----------------------------------------------------------

Write-Step 'Checking Azure CLI login'
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    throw 'Not signed in to the Azure CLI. Run: az login'
}

$subscriptionId = $account.id
Write-Skip "subscription: $($account.name) ($subscriptionId)"
Write-Skip "tenant:       $($account.tenantId)"

if ([string]::IsNullOrWhiteSpace($StorageAccountName)) {
    # Deterministic, so re-running produces the same name without storing it anywhere.
    $hash = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($subscriptionId))
    $suffix = -join ($hash[0..3] | ForEach-Object { $_.ToString('x2') })
    $StorageAccountName = "stcjtfstate$suffix"
}

Write-Skip "storage account: $StorageAccountName"

# --- Resource group ---------------------------------------------------------

Write-Step "Resource group '$ResourceGroupName'"
if ((az group exists --name $ResourceGroupName) -eq 'true') {
    Write-Skip 'already exists'
} else {
    az group create --name $ResourceGroupName --location $Location --output none
    Write-Skip 'created'
}

# --- Storage account --------------------------------------------------------

Write-Step "Storage account '$StorageAccountName'"
$existing = az storage account show `
    --name $StorageAccountName `
    --resource-group $ResourceGroupName `
    --output json 2>$null

if ($existing) {
    Write-Skip 'already exists'
} else {
    # Locally redundant is the right choice for state: it is small, it is recreatable
    # from the resources it describes in the worst case, and this is a $0-5/month project.
    # Blob versioning is the part that actually matters, because it turns a corrupted
    # state file from a catastrophe into an inconvenience.
    az storage account create `
        --name $StorageAccountName `
        --resource-group $ResourceGroupName `
        --location $Location `
        --sku Standard_LRS `
        --kind StorageV2 `
        --min-tls-version TLS1_2 `
        --allow-blob-public-access false `
        --output none

    az storage account blob-service-properties update `
        --account-name $StorageAccountName `
        --resource-group $ResourceGroupName `
        --enable-versioning true `
        --output none

    Write-Skip 'created, with blob versioning enabled'
}

# --- State container --------------------------------------------------------

Write-Step "Container '$ContainerName'"
$containerExists = az storage container exists `
    --name $ContainerName `
    --account-name $StorageAccountName `
    --auth-mode login `
    --query exists `
    --output tsv 2>$null

if ($containerExists -eq 'true') {
    Write-Skip 'already exists'
} else {
    az storage container create `
        --name $ContainerName `
        --account-name $StorageAccountName `
        --auth-mode login `
        --output none
    Write-Skip 'created'
}

# --- CI/CD role assignment --------------------------------------------------

if (-not [string]::IsNullOrWhiteSpace($CicdClientId)) {
    Write-Step "Granting Contributor to CI/CD app $CicdClientId"

    $scope = "/subscriptions/$subscriptionId"
    $assigned = az role assignment list `
        --assignee $CicdClientId `
        --role Contributor `
        --scope $scope `
        --output tsv 2>$null

    if ($assigned) {
        Write-Skip 'already assigned'
    } else {
        az role assignment create `
            --assignee $CicdClientId `
            --role Contributor `
            --scope $scope `
            --output none
        Write-Skip 'assigned'
    }
} else {
    Write-Step 'Skipping CI/CD role assignment'
    Write-Skip 'Pass -CicdClientId once infra/terraform/identity has created it.'
}

# --- Output -----------------------------------------------------------------

Write-Host ''
Write-Host 'Bootstrap complete. Backend configuration:' -ForegroundColor Green
Write-Host ''
Write-Host @"
  terraform {
    backend "azurerm" {
      resource_group_name  = "$ResourceGroupName"
      storage_account_name = "$StorageAccountName"
      container_name       = "$ContainerName"
      key                  = "platform.tfstate"
    }
  }
"@
Write-Host ''
Write-Host 'To migrate the identity module off local state:' -ForegroundColor Green
Write-Host '  cd infra/terraform/identity && terraform init -migrate-state'
Write-Host ''
