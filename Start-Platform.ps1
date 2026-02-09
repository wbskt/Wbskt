# Start-Platform.ps1
$ErrorActionPreference = "Stop"

Write-Host "--- WBSKT PLATFORM LAUNCHER ---" -ForegroundColor Cyan

# 1. Clean up any previous runs
$credsFile = "Webskt.Simulator/wbskt_creds.json"
if (Test-Path $credsFile) {
    Remove-Item $credsFile
    Write-Host "Cleared old simulator credentials." -ForegroundColor Gray
}

# 2. Start Services in the background
Write-Host "Starting Auth Host [7000]..." -ForegroundColor Yellow
$AuthProc = Start-Process dotnet -ArgumentList "run --project Webskt.Auth.Host/Webskt.Auth.Host.csproj" -PassThru

Write-Host "Starting Management Host [7010]..." -ForegroundColor Yellow
$MgmtProc = Start-Process dotnet -ArgumentList "run --project Webskt.Management.Host/Webskt.Management.Host.csproj" -PassThru

Write-Host "Starting Socket Host [7020]..." -ForegroundColor Yellow
$SockProc = Start-Process dotnet -ArgumentList "run --project Webskt.Socket.Host/Webskt.Socket.Host.csproj" -PassThru

Write-Host "Waiting 10 seconds for services to initialize..." -ForegroundColor Gray
Start-Sleep -Seconds 10

# 3. Start the Simulator in the foreground
Write-Host "Launching Simulator..." -ForegroundColor Green
dotnet run --project Webskt.Simulator/Webskt.Simulator.csproj

# 4. Cleanup on exit
Write-Host "Shutting down services..." -ForegroundColor Red
Stop-Process -Id $AuthProc.Id -ErrorAction SilentlyContinue
Stop-Process -Id $MgmtProc.Id -ErrorAction SilentlyContinue
Stop-Process -Id $SockProc.Id -ErrorAction SilentlyContinue
