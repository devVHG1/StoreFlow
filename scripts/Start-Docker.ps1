$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Установіть Docker Desktop із Linux-контейнерами.' }
function Test-DockerEngine {
    $probe = [Diagnostics.Process]::new()
    $probe.StartInfo.FileName = 'docker'
    $probe.StartInfo.UseShellExecute = $false
    $probe.StartInfo.CreateNoWindow = $true
    $probe.StartInfo.RedirectStandardOutput = $true
    $probe.StartInfo.RedirectStandardError = $true
    foreach ($argument in @('version','--format','{{.Server.Version}}')) { $probe.StartInfo.ArgumentList.Add($argument) }
    try {
        $null = $probe.Start()
        $output = $probe.StandardOutput.ReadToEndAsync()
        $errors = $probe.StandardError.ReadToEndAsync()
        if (-not $probe.WaitForExit(10000)) { $probe.Kill($true); $probe.WaitForExit(); return $false }
        return $probe.ExitCode -eq 0
    }
    finally { $probe.Dispose() }
}
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'Initialize-DockerEnv.ps1')
    if (-not (Test-DockerEngine)) {
        $desktop = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
        if ($IsWindows -and (Test-Path -LiteralPath $desktop)) {
            Write-Output 'Запускаємо Docker Desktop…'
            Start-Process -FilePath $desktop -WindowStyle Hidden
        }
        else { Write-Output 'Очікуємо готовності Docker…' }
        $ready = $false
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while ($deadline.Elapsed.TotalSeconds -lt 60) {
            if (Test-DockerEngine) { $ready = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw 'Docker ще не готовий. Перевірте Docker Desktop і повторіть команду.' }
    }
    docker compose config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Некоректні налаштування Docker Compose.' }
    docker compose up --build -d
    if ($LASTEXITCODE -ne 0) { throw 'Стек не запустився. Перегляньте docker compose logs --tail 100.' }
    $lastError = ''
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            & (Join-Path $PSScriptRoot 'Test-DockerStack.ps1')
            $binding = docker compose port api 8080
            if ($LASTEXITCODE -ne 0) { throw 'Не знайдено порт API.' }
            $address = 'http://' + ($binding | Select-Object -First 1)
            foreach ($path in '/', '/manager.html', '/panel.js', '/shop.js', '/shop.css') {
                $null = Invoke-WebRequest ($address + $path) -TimeoutSec 5
            }
            Write-Output "Магазин: $address/"
            Write-Output "Панель команди: $address/manager.html"
            return
        }
        catch { $lastError = $_.Exception.Message; Start-Sleep -Seconds 2 }
    }
    throw "Не вдалося підтвердити готовність стека: $lastError"
}
finally { Pop-Location }
