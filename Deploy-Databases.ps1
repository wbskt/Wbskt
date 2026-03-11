<#
.SYNOPSIS
    Builds and deploys the WBSKT databases.
    Works on Windows, macOS, and Linux.

.DESCRIPTION
    1. Builds Wbskt.Database.Auth and Wbskt.Database projects.
    2. Publishes the resulting DACPACs to the specified SQL Server.

.PARAMETER Server
    The target SQL Server instance. Defaults to "localhost".

.PARAMETER User
    The SQL Server username. Defaults to "sa".

.PARAMETER Password
    The SQL Server password. Defaults to "Welcome1234".

.EXAMPLE
    ./Deploy-Databases.ps1
    Deploys to localhost with default credentials.

.EXAMPLE
    ./Deploy-Databases.ps1 -Server "192.168.1.100" -Password "MySecretPass"
#>

param (
    [Parameter(Mandatory = $false)]
    [switch]$Fresh, # Pass this to delete and redeploy fresh
    [string]$Server = "localhost",
    [string]$User = "sa",
    [string]$Password = "Welcome1234"
)

$ErrorActionPreference = "Stop"

# --- Helper Functions ---

function Get-ScriptDirectory {
    if ($PSScriptRoot) { return $PSScriptRoot }
    return Split-Path -Parent $MyInvocation.MyCommand.Definition
}

function Test-Command ($command) {
    return (Get-Command $command -ErrorAction SilentlyContinue) -ne $null
}

# --- Main Script ---

$RootDir = Get-ScriptDirectory

Write-Host "Checking prerequisites..." -ForegroundColor Cyan

if (-not (Test-Command "dotnet")) {
    Write-Error "dotnet CLI is not installed or not in PATH."
}

if (-not (Test-Command "sqlpackage")) {
    Write-Warning "sqlpackage is not in PATH. Attempting to install via dotnet tool..."
    try {
        dotnet tool install -g microsoft.sqlpackage
        Write-Host "sqlpackage installed successfully." -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not install sqlpackage. Please ensure it is installed and available in PATH."
    }
}

# Define Projects (Relative Paths)
$Projects = @(
    @{
        Name = "Wbskt.Database.Auth";
        Path = "Databases/Wbskt.Database.Auth/Wbskt.Database.Auth.sqlproj";
        DbName = "Wbskt.Database.Auth"
    },
    @{
        Name = "Wbskt.Database";
        Path = "Databases/Wbskt.Database/Wbskt.Database.sqlproj";
        DbName = "Wbskt.Database"
    }
)

# Connection String Builder
$ConnString = "Data Source=$Server;User ID=$User;PWD=$Password;Persist Security Info=True;Pooling=False;Connect Timeout=60;Encrypt=False;Trust Server Certificate=True"

foreach ($Proj in $Projects) {
    Write-Host "`n--------------------------------------------------" -ForegroundColor Cyan
    Write-Host "Processing $($Proj.Name)..." -ForegroundColor Cyan
    Write-Host "--------------------------------------------------" -ForegroundColor Cyan

    $ProjPath = Join-Path $RootDir $Proj.Path
    
    # 1. Build
    Write-Host "Building project..." -ForegroundColor Yellow
    dotnet build $ProjPath -c Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $($Proj.Name)."
    }

    # 2. Locate Dacpac
    # Note: The output path might vary slightly depending on SDK version, so we search for it.
    $DacpacName = "$($Proj.Name).dacpac"
    $DacpacFile = Get-ChildItem -Path (Join-Path (Split-Path $ProjPath) "bin") -Filter $DacpacName -Recurse | 
                  Sort-Object LastWriteTime -Descending | 
                  Select-Object -First 1

    if (-not $DacpacFile) {
        Write-Error "Could not find built DACPAC for $($Proj.Name)."
    }

    Write-Host "Found DACPAC: $($DacpacFile.FullName)" -ForegroundColor Gray

    # 3. Publish
    Write-Host "Deploying to $Server / $($Proj.DbName)..." -ForegroundColor Yellow
    
    # Construct arguments explicitly
    $SqlPackageArgs = @(
        "/Action:Publish",
        "/SourceFile:$($DacpacFile.FullName)",
        "/TargetConnectionString:$ConnString;Database=$($Proj.DbName)"
    )
    if ($Fresh) {
        $SqlPackageArgs += "/p:CreateNewDatabase=True"
        Write-Host "Creating fresh database..." -ForegroundColor Yellow
    }

    # Print the command for debugging (masking password)
    $SafeConnString = $ConnString.Replace($Password, "****")
    Write-Host "Executing: sqlpackage /Action:Publish /TargetConnectionString:`"$SafeConnString;Database=$($Proj.DbName)`" ..." -ForegroundColor DarkGray

    # Execute sqlpackage
    # We use Invoke-Expression or direct execution depending on shell, but passing args array is safest in PS
    & sqlpackage $SqlPackageArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Deployment failed for $($Proj.Name)."
    }

    Write-Host "Successfully deployed $($Proj.DbName)." -ForegroundColor Green
}

Write-Host "`nAll databases deployed successfully!" -ForegroundColor Green
