param([ValidatePattern('^[a-f0-9]{32}$')][string]$ExpectedFingerprint)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Database-Tools.ps1')
$root = Split-Path $PSScriptRoot -Parent
$tag = [Guid]::NewGuid().ToString('N')
$temporaryDatabase = "storeflow_restore_qa_$tag"
$containerArchive = "/tmp/storeflow-$tag.dump"
$backupFolder = Join-Path $root 'backups'
$null = New-Item -ItemType Directory -Path $backupFolder -Force
$archive = Join-Path $backupFolder ("storeflow-docker-{0}-$tag.dump" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$created = $false
Push-Location $root
try {
    docker compose exec -T db sh -eu -c 'exec pg_dump --no-password --format=custom --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" --file="$1"' sh $containerArchive
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося створити копію Docker-бази.' }
    docker compose cp "db:$containerArchive" $archive | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося зберегти архів на диску.' }
    docker compose exec -T db sh -eu -c 'exec createdb --no-password --username="$POSTGRES_USER" "$1"' sh $temporaryDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося створити тимчасову базу для перевірки.' }
    $created = $true
    docker compose exec -T db sh -eu -c 'exec pg_restore --no-password --exit-on-error --no-owner --no-privileges --username="$POSTGRES_USER" --dbname="$1" "$2"' sh $temporaryDatabase $containerArchive
    if ($LASTEXITCODE -ne 0) { throw 'Перевірка відновлення Docker-бази завершилася помилкою.' }
    $sql = Get-StoreFlowFingerprintSql
    $result = $sql | docker compose exec -T db sh -eu -c 'export PGOPTIONS="-c timezone=UTC"; exec psql --no-password -X -At --set=ON_ERROR_STOP=1 --username="$POSTGRES_USER" --dbname="$1"' sh $temporaryDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося перевірити дані відновленої бази.' }
    $fingerprint = ($result -join '').Trim()
    if ($fingerprint -notmatch '^[a-f0-9]{32}$') { throw 'Некоректний результат перевірки даних.' }
    if ($ExpectedFingerprint -and $ExpectedFingerprint -ne $fingerprint) { throw 'Дані відновленої бази не збігаються з очікуваними.' }
    [pscustomobject]@{ Path=$archive; Sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash; Restored=$true; DataFingerprint=$fingerprint; TemporaryDatabase=$temporaryDatabase }
}
finally {
    try {
        if ($created) {
            # Видаляємо лише базу, створену цією перевіркою.
            if ($temporaryDatabase -notmatch '^storeflow_restore_qa_[a-f0-9]{32}$') { throw 'Некоректна назва тимчасової бази.' }
            docker compose exec -T db sh -eu -c 'exec dropdb --no-password --if-exists --force --username="$POSTGRES_USER" "$1"' sh $temporaryDatabase
            if ($LASTEXITCODE -ne 0) { throw 'Не вдалося прибрати тимчасову базу.' }
        }
    }
    finally {
        docker compose exec -T db rm -f -- $containerArchive | Out-Null
        $cleanupCode = $LASTEXITCODE
        Pop-Location
        if ($cleanupCode -ne 0) { Write-Warning "Не вдалося прибрати тимчасовий архів $containerArchive у контейнері." }
    }
}

