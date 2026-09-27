# Portion AI - Start All Required Services
# Checks each required service and starts it if not already running.

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Starting Portion AI Required Services  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. PostgreSQL Service (Port 5433)
$pgPort = 5433
$pgConnected = Test-NetConnection -ComputerName localhost -Port $pgPort -InformationLevel Quiet
if ($pgConnected) {
    Write-Host "[+] PostgreSQL is already running on port $pgPort." -ForegroundColor Green
} else {
    Write-Host "[*] PostgreSQL not detected on port $pgPort. Attempting to start service..." -ForegroundColor Yellow
    $pgService = Get-Service | Where-Object Name -like "*postgres*" | Select-Object -First 1
    if ($pgService) {
        Start-Service $pgService.Name
        Write-Host "[+] Started service $($pgService.Name)" -ForegroundColor Green
    } else {
        Write-Host "[-] PostgreSQL service not found. Please ensure PostgreSQL with pgvector is installed." -ForegroundColor Red
    }
}

# 2. Ollama Local AI Service (Port 11434)
$ollamaPort = 11434
$ollamaConnected = Test-NetConnection -ComputerName localhost -Port $ollamaPort -InformationLevel Quiet
if ($ollamaConnected) {
    Write-Host "[+] Ollama service is already running on port $ollamaPort." -ForegroundColor Green
} else {
    Write-Host "[*] Starting Ollama serve..." -ForegroundColor Yellow
    Start-Process ollama -ArgumentList "serve" -WindowStyle Hidden
    Start-Sleep -Seconds 3
    Write-Host "[+] Ollama service launched." -ForegroundColor Green
}

# 3. ASP.NET Core Backend Web API (Port 5000)
$backendPort = 5000
$backendConnected = Test-NetConnection -ComputerName localhost -Port $backendPort -InformationLevel Quiet
if ($backendConnected) {
    Write-Host "[+] Portion Web API is already running on port $backendPort." -ForegroundColor Green
} else {
    Write-Host "[*] Starting Portion Backend Web API on http://localhost:5000..." -ForegroundColor Yellow
    Start-Process dotnet -ArgumentList "run --project Portion.Server/Portion.Server.csproj" -WorkingDirectory $PSScriptRoot -WindowStyle Normal
    Write-Host "[+] Backend Web API process launched." -ForegroundColor Green
}

# 4. React Vite Frontend UI (Port 5173)
$uiPort = 5173
$uiConnected = Test-NetConnection -ComputerName localhost -Port $uiPort -InformationLevel Quiet
if ($uiConnected) {
    Write-Host "[+] Portion Frontend UI is already running on port $uiPort." -ForegroundColor Green
} else {
    Write-Host "[*] Starting Portion React UI on http://localhost:5173..." -ForegroundColor Yellow
    Start-Process npm.cmd -ArgumentList "run dev" -WorkingDirectory "$PSScriptRoot\portion-ui" -WindowStyle Normal
    Write-Host "[+] React UI dev server launched." -ForegroundColor Green
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " All services checked!                 " -ForegroundColor Cyan
Write-Host " App UI: http://localhost:5173         " -ForegroundColor Green
Write-Host " Backend API: http://localhost:5000    " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
