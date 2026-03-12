<#PSScriptInfo
.VERSION 1.2.1
.GUID f3a1d2c4-5e9b-4d8a-a6b3-2f8e4d0c9b4c
.AUTHOR Richard Joy
.COMPANYNAME WBSKT Inc.
.COPYRIGHT Copyright (C) WBSKT Inc.
.TAGS PowerShell, Database, Development, Wbskt
.DESCRIPTION Deploys WBSKT databases. Only drops-and-recreates when -Fresh is used.
#>

#Requires -Version 5.1

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
    [string] $Password = "Welcome1234"
)

begin {
    Set-StrictMode -Version 1
    $Script:ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop

    $authDbName = "Wbskt.Database.Auth"
    $coreDbName = "Wbskt.Database"
    $projectRoot = $PSScriptRoot

    # Projects Configuration
    $authProject = Join-Path $projectRoot "Databases\Wbskt.Database.Auth\Wbskt.Database.Auth.sqlproj"
    $coreProject = Join-Path $projectRoot "Databases\Wbskt.Database\Wbskt.Database.sqlproj"

    function Get-SqlPackagePath {
        $paths = @(
            "C:\Program Files\Microsoft SQL Server\170\DAC\bin\SqlPackage.exe",
            "C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
            "C:\Program Files\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
            "$env:USERPROFILE\.dotnet\tools\sqlpackage.exe"
        )

        foreach ($path in $paths) {
            
            # Check if the path exists
            if (Test-Path $path) {
                return $path
            }
        }

        $fromPath = Get-Command "SqlPackage.exe" -ErrorAction SilentlyContinue
        
        # Fallback to system path
        if ($fromPath) {
            return $fromPath.Source
        }

        return $null
    }

    function Drop-Database {
        param([string]$DbName)

        Write-Host "Dropping database $DbName (Fresh Start)..." -ForegroundColor Yellow
        
        # We connect to master to perform the drop
        $masterConnStr = "Data Source=$Server;Database=master;User ID=$User;PWD=$Password;Encrypt=False;"
        
        # SQL to kick everyone out and drop the hammer
        $sql = "
            IF EXISTS (SELECT name FROM sys.databases WHERE name = '$DbName')
            BEGIN
                ALTER DATABASE [$DbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [$DbName];
            END"

        # Use ADO.NET to avoid dependency on SQLPS or SqlServer modules
        $connection = New-Object System.Data.SqlClient.SqlConnection($masterConnStr)
        
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = $sql
            $null = $command.ExecuteNonQuery()
            Write-Host "Database $DbName dropped successfully." -ForegroundColor Gray
        }
        finally {
            $connection.Close()
        }
    }

    function Get-DacpacPath {
        param(
            [string]$ProjectPath, 
            [string]$DacpacName
        )

        $targetDir = Join-Path (Split-Path $ProjectPath) "bin\Release"
        
        # Verify directory exists
        if (-not (Test-Path $targetDir)) {
            return $null
        }

        $dacpac = Get-ChildItem -Path $targetDir -Filter $DacpacName -Recurse | 
                  Sort-Object LastWriteTime -Descending | 
                  Select-Object -First 1

        # Return full path if found
        if ($dacpac) {
            return $dacpac.FullName
        }

        return $null
    }

    function Publish-Database {
        param(
            [string]$DbName,
            [string]$DacpacPath,
            [string]$SqlPackageExecutable,
            [bool]$Recreate
        )

        # Ensure we have a valid source file
        if (-not $DacpacPath) {
            throw "DACPAC path is null for $DbName. Did the build fail?"
        }

        Write-Host "`n--- Publishing Database: $DbName ---" -ForegroundColor Cyan
        
        $connString = "Data Source=$Server;Database=$DbName;Persist Security Info=True;User ID=$User;PWD=$Password;Pooling=False;Encrypt=False;Trust Server Certificate=True;Connect Timeout=60;"

        $publishArgs = @(
            "/Action:Publish",
            "/SourceFile:$DacpacPath",
            "/TargetConnectionString:$connString",
            "/p:BlockOnPossibleDataLoss=False",
            "/p:CreateNewDatabase=$Recreate"
        )

        & $SqlPackageExecutable @publishArgs
        
        # Check exit code for failures
        if ($LASTEXITCODE -ne 0) {
            throw "SqlPackage failed with exit code $LASTEXITCODE for $DbName"
        }

        Write-Host "Database $DbName published successfully." -ForegroundColor Green
    }
}

process {
    Write-Host "--- Initializing Database Deployment ---" -ForegroundColor Cyan

    # 1. Locate SqlPackage
    $exe = Get-SqlPackagePath
    
    # Verify tool availability
    if (-not $exe) {
        throw "SqlPackage.exe not found. Please install SSDT or the sqlpackage dotnet tool."
    }

    # 2. Build Projects
    Write-Host "Building SQL Projects..." -ForegroundColor Gray
    
    $projects = @($authProject, $coreProject)
    
    foreach ($project in $projects) {
        dotnet build $project -c Release
        
        # Stop if build fails
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for: $project"
        }
    }

    # 3. Handle Fresh Start (Drop step)
    if ($Fresh) {
        Drop-Database -DbName $authDbName
        Drop-Database -DbName $coreDbName
    }

    # 4. Deploy Auth
    $authDacpac = Get-DacpacPath -ProjectPath $authProject -DacpacName "Wbskt.Database.Auth.dacpac"
    Publish-Database -DbName $authDbName -DacpacPath $authDacpac -SqlPackageExecutable $exe -Recreate $Fresh

    # 5. Deploy Core
    $coreDacpac = Get-DacpacPath -ProjectPath $coreProject -DacpacName "Wbskt.Database.dacpac"
    Publish-Database -DbName $coreDbName -DacpacPath $coreDacpac -SqlPackageExecutable $exe -Recreate $Fresh

    Write-Host "`nDeployment completed successfully." -ForegroundColor Green
}