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

    The state account is deliberately project-neutral and shared by every project in the
    subscription. State files are separated by the backend's `key`, not by account, so a
    second project runs this same script unchanged and only picks a different key.

    Idempotent. Safe to run repeatedly; existing resources are left alone.

.EXAMPLE
    ./bootstrap.ps1

.EXAMPLE
    ./bootstrap.ps1 -CicdClientId 00000000-0000-0000-0000-000000000000

.EXAMPLE
    ./bootstrap.ps1 -Project someotherproject
#>
[CmdletBinding()]
param(
    [string] $Location = 'centralus',

    # Names the state file, not the shared infrastructure. Everything this script creates
    # is reused across projects; only the printed backend `key` changes with this.
    [string] $Project = 'codejudge',

    [string] $ResourceGroupName = 'rg-tfstate',

    # Storage account names are globally unique across all of Azure, 3 to 24 characters,
    # lowercase alphanumeric only. Left empty, a deterministic name is derived from the
    # subscription id so repeated runs agree without needing the value written down.
    # Keyed on the subscription rather than the project, because one account serves them all.
    [string] $StorageAccountName = '',

    [string] $ContainerName = 'tfstate',

    # Client id of the CI/CD app registration created by infra/terraform/identity.
    # Supply it to grant Contributor on the subscription. Omit to skip that step.
    [string] $CicdClientId = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Skip { param([string] $Message) Write-Host "    $Message" -ForegroundColor DarkGray }

<#
.SYNOPSIS
    Runs an `az` existence check that is allowed to fail.
.DESCRIPTION
    Every "does this already exist?" probe here fails by design on a first run, and `az`
    reports that by writing to stderr. Under Windows PowerShell 5.1 a native command
    writing to stderr while $ErrorActionPreference is 'Stop' raises a terminating
    NativeCommandError, so the probe throws instead of answering "no". PowerShell 7 does
    not do this, which is exactly why it goes unnoticed until someone runs powershell.exe.

    Returns $null when the resource is absent or the command failed, so callers can treat
    the result as a plain boolean.
#>
function Invoke-AzProbe {
    param([Parameter(Mandatory)][scriptblock] $Command)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Command 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return $output
    } finally {
        $ErrorActionPreference = $previous
    }
}

# --- Preconditions ----------------------------------------------------------

Write-Step 'Checking Azure CLI login'
$accountJson = Invoke-AzProbe { az account show --output json }
if (-not $accountJson) {
    throw 'Not signed in to the Azure CLI. Run: az login'
}
$account = $accountJson | ConvertFrom-Json

$subscriptionId = $account.id
Write-Skip "subscription: $($account.name) ($subscriptionId)"
Write-Skip "tenant:       $($account.tenantId)"

if ([string]::IsNullOrWhiteSpace($StorageAccountName)) {
    # Deterministic, so re-running produces the same name without storing it anywhere.
    #
    # ComputeHash on an instance rather than the static SHA256::HashData: the latter is
    # .NET 5+, so it throws MethodNotFound under Windows PowerShell 5.1, which is still
    # what `powershell.exe` launches on a stock Windows box.
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($subscriptionId))
    } finally {
        $sha256.Dispose()
    }
    $suffix = -join ($hash[0..3] | ForEach-Object { $_.ToString('x2') })
    $StorageAccountName = "sttfstate$suffix"
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
$existing = Invoke-AzProbe {
    az storage account show `
        --name $StorageAccountName `
        --resource-group $ResourceGroupName `
        --output json
}

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
$containerExists = Invoke-AzProbe {
    az storage container exists `
        --name $ContainerName `
        --account-name $StorageAccountName `
        --auth-mode login `
        --query exists `
        --output tsv
}

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
    $assigned = Invoke-AzProbe {
        az role assignment list `
            --assignee $CicdClientId `
            --role Contributor `
            --scope $scope `
            --output tsv
    }

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
      key                  = "$Project-platform.tfstate"
    }
  }
"@
Write-Host ''
Write-Host 'The key is what separates projects. This account is shared; use' -ForegroundColor Green
Write-Host "  <project>-platform.tfstate  and  <project>-identity.tfstate"
Write-Host ''
Write-Host 'To migrate the identity module off local state:' -ForegroundColor Green
Write-Host "  cd infra/terraform/identity && terraform init -migrate-state -backend-config=`"key=$Project-identity.tfstate`""
Write-Host ''
