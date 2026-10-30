param([Parameter(Mandatory)][string]$BackupPath, [ValidatePattern('^[a-fA-F0-9]{32}$')][string]$ExpectedFingerprint)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Database-Tools.ps1')
$settings = Get-StoreFlowDatabase
$path = (Resolve-Path -LiteralPath $BackupPath).Path
if ((Get-Item -LiteralPath $path).Length -eq 0) { throw 'Архів порожній.' }
$restore = Find-PostgresTool pg_restore
$null = & $restore --list $path
if ($LASTEXITCODE -ne 0) { throw 'Файл не є коректним архівом PostgreSQL.' }
$target = 'storeflow_restore_qa_' + [guid]::NewGuid().ToString('N')
$created = $false
try {
    Invoke-StoreFlowPostgres -Settings $settings -Tool createdb -Arguments @('--maintenance-db','postgres','--',$target)
    $created = $true
    Invoke-StoreFlowPostgres -Settings $settings -Tool pg_restore -Arguments @('--dbname',$target,'--exit-on-error','--no-owner','--no-privileges',$path)
    $fingerprint = (Invoke-StoreFlowPostgres -Settings $settings -Tool psql -Arguments @('--dbname',$target,'-X','-A','-t','-v','ON_ERROR_STOP=1','-c',(Get-StoreFlowFingerprintSql)) | Out-String).Trim()
    if ($fingerprint -notmatch '^[a-f0-9]{32}$') { throw 'Не вдалося перевірити таблиці відновленої бази.' }
    if ($ExpectedFingerprint -and $fingerprint -ne $ExpectedFingerprint.ToLowerInvariant()) { throw 'Дані відновленої бази відрізняються від очікуваного знімка.' }
    [pscustomobject]@{ Restored = $true; DataFingerprint = $fingerprint; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; TemporaryDatabase = $target }
}
finally {
    if ($created) {
        if ($target -notmatch '^storeflow_restore_qa_[a-f0-9]{32}$') { throw 'Некоректна назва тимчасової бази; видалення не виконано.' }
        $null = Invoke-StoreFlowPostgres -Settings $settings -Tool dropdb -Arguments @('--maintenance-db','postgres','--if-exists','--force','--',$target)
    }
}
