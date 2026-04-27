param(
    [string]$MigrationName = "",
    [switch]$SkipDatabaseUpdate
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,
        [string]$FailureMessage = "Command failed."
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Command
        if ($LASTEXITCODE -ne 0) {
            throw "$FailureMessage Exit code: $LASTEXITCODE"
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

Push-Location $PSScriptRoot
try {
    Write-Host "Checking EF Core model changes..."
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $pendingOutput = & dotnet ef migrations has-pending-model-changes --no-color --prefix-output 2>&1
        $pendingExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $pendingText = ($pendingOutput | Out-String).Trim()

    if ($pendingExitCode -eq 0) {
        Write-Host "No entity/model changes detected. Applying any existing pending migrations..."
    }
    elseif ($pendingText -match "Changes have been made to the model since the last migration") {
        if ([string]::IsNullOrWhiteSpace($MigrationName)) {
            $MigrationName = "Auto_" + (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
        }

        if ($MigrationName -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
            throw "MigrationName must be a valid C# identifier, for example AddUserProfileFields."
        }

        Write-Host "Model changes detected. Creating migration '$MigrationName'..."
        Invoke-NativeCommand `
            -Command { dotnet ef migrations add $MigrationName --output-dir Data/Migrations } `
            -FailureMessage "Failed to create EF Core migration."
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($pendingText)) {
            Write-Host $pendingText
        }
        throw "Unable to check EF Core model changes. Exit code: $pendingExitCode"
    }

    if ($SkipDatabaseUpdate) {
        Write-Host "Skipping database update because -SkipDatabaseUpdate was supplied."
        return
    }

    Write-Host "Applying migrations to the configured PostgreSQL/Supabase database..."
    Invoke-NativeCommand `
        -Command { dotnet ef database update } `
        -FailureMessage "Failed to update the configured PostgreSQL database."

    Write-Host "Database migration complete."
}
finally {
    Pop-Location
}
