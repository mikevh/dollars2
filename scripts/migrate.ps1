<#
.SYNOPSIS
    Applies the Dollars2 numbered raw-SQL migrations against a target database.

.DESCRIPTION
    Enumerates backend/Dollars2.Api/Migrations/*.sql, sorts them by filename (ordinal),
    and executes each in order via sqlcmd. Every script guards on its own Migrations-table
    row, so this runner is re-runnable: against a fully-migrated database it makes no changes.

    This is the manual production migration step. For a database that was migrated before the
    scripts were normalized (rows only for 006-010), run scripts/backfill_migrations.sql ONCE
    first so the pure-guard scripts cleanly no-op.

    Requires sqlcmd on PATH (part of the SQL Server command-line tools / Go sqlcmd).

.PARAMETER Server
    SQL Server host (and optional instance/port), e.g. "localhost" or "10.0.0.5,1433".

.PARAMETER Database
    Target database name.

.PARAMETER Username
    SQL login. Omit to use integrated (Windows) authentication.

.PARAMETER Password
    Password for the SQL login. Required when -Username is given.

.EXAMPLE
    ./scripts/migrate.ps1 -Server localhost -Database Dollars2

.EXAMPLE
    ./scripts/migrate.ps1 -Server 10.0.0.5 -Database Dollars2 -Username sa -Password '<secret>'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [Parameter(Mandatory = $true)]
    [string]$Database,

    [string]$Username,

    [string]$Password
)

$ErrorActionPreference = 'Stop'

if ($Username -and -not $Password) {
    throw "-Password is required when -Username is supplied."
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd was not found on PATH. Install the SQL Server command-line tools first."
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$migrationsDir = Join-Path (Split-Path -Parent $scriptRoot) 'backend/Dollars2.Api/Migrations'

if (-not (Test-Path $migrationsDir)) {
    throw "Migrations directory not found at '$migrationsDir'."
}

$scripts = Get-ChildItem -Path $migrationsDir -Filter '*.sql' |
    Sort-Object -Property Name -Culture ([System.Globalization.CultureInfo]::InvariantCulture)

if ($scripts.Count -eq 0) {
    throw "No migration scripts found in '$migrationsDir'."
}

# Shared sqlcmd connection args. -b makes sqlcmd exit non-zero on a SQL error so a
# failing migration stops the run instead of being silently skipped.
$authArgs = @('-S', $Server, '-d', $Database, '-b')
if ($Username) {
    $authArgs += @('-U', $Username, '-P', $Password)
} else {
    $authArgs += '-E'
}

Write-Host "Applying $($scripts.Count) migration(s) to [$Database] on [$Server]..."

foreach ($script in $scripts) {
    Write-Host "==> $($script.Name)"
    & sqlcmd @authArgs -i $script.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Migration '$($script.Name)' failed (sqlcmd exit code $LASTEXITCODE)."
    }
}

Write-Host "Migrations applied successfully."
