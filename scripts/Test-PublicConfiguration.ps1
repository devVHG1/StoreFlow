$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'appsettings*.json' | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|backups|\.git)[\\/]' -and $_.Name -notlike '*Local.json'
}
foreach ($file in $files) {
    $settings = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    foreach ($entry in $settings.ConnectionStrings.PSObject.Properties) {
        if ([string]$entry.Value -match '(?i)(?:^|;)\s*(Password|Pwd)\s*=\s*[^;\s]+') {
            throw "Приберіть пароль підключення з $($file.Name)."
        }
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$settings.Jwt.Key)) {
        throw "Приберіть ключ JWT з $($file.Name)."
    }
}
if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    $tracked = git -C $root ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося перевірити індекс Git.' }
    foreach ($path in $tracked) {
        if ($path -match '(^|/)(\.env($|\.)|secrets\.json$|backups/|\.secrets/)' -and $path -ne '.env.example') {
            throw "Секретний або резервний файл потрапив до Git: $path"
        }
        if ($path -match '\.(pfx|pem|key|dump)$') { throw "Приберіть приватний файл із Git: $path" }
    }
}
Write-Output 'Публічні конфігурації перевірено; паролів PostgreSQL і ключів JWT немає.'
