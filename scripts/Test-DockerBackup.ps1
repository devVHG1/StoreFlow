$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Database-Tools.ps1')
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    # Під час перевірки CI дані не повинні змінюватися.
    docker compose stop api | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося призупинити тестове API.' }
    $fingerprint = (Get-StoreFlowFingerprintSql | docker compose exec -T db sh -eu -c 'export PGOPTIONS="-c timezone=UTC"; exec psql --no-password -X -At --set=ON_ERROR_STOP=1 --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"') -join ''
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося прочитати тестову базу.' }
    $backup = & (Join-Path $PSScriptRoot 'Backup-DockerDatabase.ps1') -ExpectedFingerprint $fingerprint.Trim()
    if (-not $backup.Restored) { throw 'Не підтверджене відновлення.' }
    $databases = 'SELECT datname FROM pg_database;' | docker compose exec -T db sh -eu -c 'exec psql --no-password -X -At --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"'
    if ($LASTEXITCODE -ne 0 -or $databases -contains $backup.TemporaryDatabase) { throw 'Тимчасова база залишилась після перевірки.' }
    Write-Output 'PASS Docker backup: archive restored, data matched, temporary database removed.'
}
finally {
    docker compose start api | Out-Null
    Pop-Location
}
