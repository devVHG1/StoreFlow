$ErrorActionPreference = 'Stop'
$target = Join-Path (Split-Path $PSScriptRoot -Parent) '.env'
if (Test-Path -LiteralPath $target) {
    Write-Output '.env уже існує; наявні секрети не змінено.'
    return
}
$password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
@"
POSTGRES_DB=storeflow
POSTGRES_USER=storeflow
POSTGRES_PASSWORD=$password
JWT_KEY=$key
API_PORT=8080
"@ | Set-Content -LiteralPath $target -Encoding utf8
$password = $null
$key = $null
Write-Output 'Створено локальний .env із випадковими секретами. Файл виключений із Git і Docker build context.'
