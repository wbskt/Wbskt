<#PSScriptInfo
.VERSION 1.2.1
.GUID f3a1d2c4-5e9b-4d8a-a6b3-2f8e4d0c9b4c
.AUTHOR Richard Joy
.COMPANYNAME WBSKT Inc.
.COPYRIGHT Copyright (C) WBSKT Inc.
.TAGS PowerShell, Database, Development, Wbskt
.DESCRIPTION Deploys WBSKT databases. Only drops-and-recreates when -Fresh is used.
#>

#Requires -Version 7.0

[CmdletBinding(PositionalBinding = $false)]
param (
    # Pass this to delete and redeploy fresh
    [Parameter(Mandatory = $false)]
    [switch] $Fresh, 

    [Parameter(Mandatory = $false)]
    [string] $Server = "localhost",

    [Parameter(Mandatory = $false)]
    [string] $User = "sa",

    [Parameter(Mandatory = $false)]
    [string] $Password = "Welcome1234",

    # Optional: also create the per-host logins (wbskt_auth, wbskt_management, wbskt_engine), all
    # with this one password. Production gives each its own through the migrator; this is for
    # running the hosts locally, or in CI, the way they connect in production.
    [Parameter(Mandatory = $false)]
    [string] $HostLoginPassword = ""
)

begin {
    Set-StrictMode -Version 1
    $Script:ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

    $authDbName = "Wbskt.Database.Auth"
    $coreDbName = "Wbskt.Database"
    $projectRoot = $PSScriptRoot

    # Projects Configuration using Join-Path for OS-agnostic separators
    $authProject = Join-Path $projectRoot "Databases" "Wbskt.Database.Auth" "Wbskt.Database.Auth.sqlproj"
    $coreProject = Join-Path $projectRoot "Databases" "Wbskt.Database" "Wbskt.Database.sqlproj"

    function Get-SqlPackagePath {
        # Check system PATH first (best for Linux/macOS/Docker)
        $fromPath = Get-Command "sqlpackage" -ErrorAction SilentlyContinue
        if ($fromPath) { return $fromPath.Source }

        # Windows-specific fallbacks
        if ($IsWindows) {
            $windowsPaths = @(
                "C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
                "C:\Program Files\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
                "$env:USERPROFILE\.dotnet\tools\sqlpackage.exe"
            )
            foreach ($path in $windowsPaths) {
                if (Test-Path $path) { return $path }
            }
        }
        
        return $null
    }

    function Drop-Database {
        param([string]$DbName, [string]$SqlPackageExecutable)

        Write-Host "Dropping database $DbName (Fresh Start)..." -ForegroundColor Yellow
        
        # Cross-platform way: Use SqlPackage to "Script" a drop or use a Script Action
        # To keep it simple and avoid ADO.NET versioning issues on Linux, we use a simple SQL command via a temporary file
        $sql = "IF EXISTS (SELECT name FROM sys.databases WHERE name = '$DbName') BEGIN ALTER DATABASE [$DbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DbName]; END"
        $tmpFile = [System.IO.Path]::GetTempFileName()
        $sql | Out-File -FilePath $tmpFile -Encoding utf8

        try {
            # Attempt to use sqlcmd if available, otherwise this is a gap in non-windows environments
            if (Get-Command "sqlcmd" -ErrorAction SilentlyContinue) {
                & sqlcmd -S $Server -U $User -P $Password -Q $sql -b
            } else {
                Write-Warning "sqlcmd not found. Skipping Drop step. Please ensure database is clear manually or install sqlcmd."
            }
        }
        finally {
            if (Test-Path $tmpFile) { Remove-Item $tmpFile }
        }
    }

    function Get-DacpacPath {
        param([string]$ProjectPath, [string]$DacpacName)

        # Release folder on Linux/macOS is case-sensitive; bin/Release is standard
        $targetDir = Join-Path (Split-Path $ProjectPath) "bin" "Release"
        
        if (-not (Test-Path $targetDir)) { return $null }

        $dacpac = Get-ChildItem -Path $targetDir -Filter $DacpacName -Recurse | 
                  Sort-Object LastWriteTime -Descending | 
                  Select-Object -First 1

        return $dacpac.FullName
    }

    function Publish-Database {
        param(
            [string]$DbName,
            [string]$DacpacPath,
            [string]$SqlPackageExecutable,
            [bool]$Recreate,
            [string[]]$Variables = @()
        )

        if (-not $DacpacPath) { throw "DACPAC path is null for $DbName." }

        Write-Host "`n--- Publishing Database: $DbName ---" -ForegroundColor Cyan
        
        # Build Connection String (Added TrustServerCertificate for Linux/Docker defaults)
        $connString = "Server=$Server;Database=$DbName;User ID=$User;Password=$Password;Encrypt=True;TrustServerCertificate=True;Timeout=60;"

        $publishArgs = @(
            "/Action:Publish",
            "/SourceFile:$DacpacPath",
            "/TargetConnectionString:$connString",
            "/p:BlockOnPossibleDataLoss=False",
            "/p:CreateNewDatabase=$Recreate"
        )
        foreach ($variable in $Variables) {
            $publishArgs += "/v:$variable"
        }

        & $SqlPackageExecutable @publishArgs
        
        if ($LASTEXITCODE -ne 0) {
            throw "SqlPackage failed with exit code $LASTEXITCODE"
        }
    }
}

process {
    Write-Host "--- Initializing Cross-Platform Deployment ---" -ForegroundColor Cyan

    $exe = Get-SqlPackagePath
    if (-not $exe) { throw "sqlpackage tool not found. Install via 'dotnet tool install -g microsoft.sqlpackage'" }

    Write-Host "Building SQL Projects..." -ForegroundColor Gray
    $projects = @($authProject, $coreProject)
    
    foreach ($project in $projects) {
        # Using 'dotnet build' is natively cross-platform
        dotnet build $project -c Release
        if ($LASTEXITCODE -ne 0) { throw "Build failed for: $project" }
    }

    if ($Fresh) {
        Drop-Database -DbName $authDbName -SqlPackageExecutable $exe
        Drop-Database -DbName $coreDbName -SqlPackageExecutable $exe
    }

    $authVariables = @()
    $coreVariables = @()
    if ($HostLoginPassword) {
        $authVariables = @("AuthHostPassword=$HostLoginPassword")
        $coreVariables = @("ManagementHostPassword=$HostLoginPassword", "EngineHostPassword=$HostLoginPassword")
    }

    $authDacpac = Get-DacpacPath -ProjectPath $authProject -DacpacName "Wbskt.Database.Auth.dacpac"
    Publish-Database -DbName $authDbName -DacpacPath $authDacpac -SqlPackageExecutable $exe -Recreate $Fresh -Variables $authVariables

    $coreDacpac = Get-DacpacPath -ProjectPath $coreProject -DacpacName "Wbskt.Database.dacpac"
    Publish-Database -DbName $coreDbName -DacpacPath $coreDacpac -SqlPackageExecutable $exe -Recreate $Fresh -Variables $coreVariables

    Write-Host "`nDeployment completed successfully." -ForegroundColor Green
}