param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $rows = docker compose ps --all --format json
    if ($LASTEXITCODE -ne 0) { throw 'Docker недоступний. Запустіть Docker Desktop і повторіть перевірку.' }
    $services = @($rows | ForEach-Object { $_ | ConvertFrom-Json })
    $api = $services | Where-Object Service -eq 'api'
    $db = $services | Where-Object Service -eq 'db'
    $migration = $services | Where-Object Service -eq 'migrate'
    if ($api.State -ne 'running' -or $db.State -ne 'running') {
        throw 'API або PostgreSQL не запущені. Виконайте docker compose up -d.'
    }
    if ($db.Health -ne 'healthy') { throw 'PostgreSQL ще не готова. Повторіть перевірку за кілька секунд.' }
    if ($null -eq $migration -or $migration.State -ne 'exited' -or $migration.ExitCode -ne 0) {
        throw 'Міграції не завершилися успішно. Перегляньте docker compose logs migrate.'
    }

    # Порт беремо з Docker, тому налаштування API_PORT теж підтримується.
    $binding = @($api.Publishers | Where-Object { $_.TargetPort -eq 8080 -and $_.PublishedPort -gt 0 })
    if ($binding.Count -eq 0) { throw 'Не знайдено опублікований порт API.' }
    $address = 'http://localhost:' + $binding[0].PublishedPort
    $health = Invoke-RestMethod "$address/health" -TimeoutSec 10
    if ($health.status -ne 'Healthy') { throw 'API повідомляє, що база недоступна.' }
    $swagger = Invoke-RestMethod "$address/swagger/v1/swagger.json" -TimeoutSec 10
    foreach ($path in '/api/categories', '/api/products', '/api/orders', '/api/auth/login') {
        if ($path -notin $swagger.paths.PSObject.Properties.Name) { throw "У Swagger немає $path." }
    }
    $products = Invoke-RestMethod "$address/api/products?page=1&pageSize=1" -TimeoutSec 10
    if ($products.page -ne 1 -or $products.pageSize -ne 1 -or $null -eq $products.items) {
        throw 'Products повернув неочікуваний формат сторінки.'
    }
    Write-Output 'Готово: PostgreSQL, міграції, API, Swagger і читання Products працюють.'
    Write-Output "Swagger: $address/swagger"
}
finally { Pop-Location }
