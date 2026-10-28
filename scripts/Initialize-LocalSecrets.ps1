$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'StoreFlow.Аpi\StoreFlow.Аpi.csproj'
$project = [xml](Get-Content -Raw -LiteralPath $projectPath)
$secretId = [string]$project.Project.PropertyGroup.UserSecretsId
if ([string]::IsNullOrWhiteSpace($secretId)) { throw 'У проєкті відсутній UserSecretsId.' }
$secretFile = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretId\secrets.json"
$existingKey = $null
if (Test-Path -LiteralPath $secretFile) {
    $secrets = Get-Content -Raw -LiteralPath $secretFile | ConvertFrom-Json
    $existingKey = $secrets.'Jwt:Key'
    if (-not $existingKey) { $existingKey = $secrets.Jwt.Key }
}
if ($existingKey -and $existingKey.Length -ge 32) {
    Write-Output 'Локальний ключ JWT уже налаштовано.'
    return
}
$jwtKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
dotnet user-secrets set 'Jwt:Key' $jwtKey --project $projectPath
if ($LASTEXITCODE -ne 0) { throw 'Не вдалося зберегти локальний ключ JWT.' }
$jwtKey = $null
Write-Output 'Локальний ключ JWT збережено поза проєктом.'
