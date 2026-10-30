param([Parameter(Mandatory)][ValidatePattern('^storeflow_orders_qa_[a-f0-9]{32}$')][string]$SourceDatabase)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Database-Tools.ps1')
$settings = Get-StoreFlowDatabase $SourceDatabase
$expected = (Invoke-StoreFlowPostgres -Settings $settings -Tool psql -Arguments @('--dbname',$SourceDatabase,'-X','-A','-t','-v','ON_ERROR_STOP=1','-c',(Get-StoreFlowFingerprintSql)) | Out-String).Trim()
$rows = (Invoke-StoreFlowPostgres -Settings $settings -Tool psql -Arguments @('--dbname',$SourceDatabase,'-X','-A','-t','-c','SELECT count(*) FROM "OrderItems";') | Out-String).Trim()
if ([long]$rows -le 0) { throw 'Для перевірки потрібні непорожні тестові замовлення.' }
$backup = & (Join-Path $PSScriptRoot 'Backup-Database.ps1') -SourceDatabase $SourceDatabase
$restored = & (Join-Path $PSScriptRoot 'Test-DatabaseBackup.ps1') -BackupPath $backup.Path -ExpectedFingerprint $expected
if (-not $restored.Restored -or $backup.Sha256 -ne $restored.Sha256) { throw 'Відновлення не пройшло перевірку.' }
$name = $restored.TemporaryDatabase
if ($name -notmatch '^storeflow_restore_qa_[a-f0-9]{32}$') { throw 'Неочікувана назва бази відновлення.' }
$remaining = (Invoke-StoreFlowPostgres -Settings $settings -Tool psql -Arguments @('--dbname','postgres','-X','-A','-t','-c',"SELECT count(*) FROM pg_database WHERE datname='$name';") | Out-String).Trim()
if ($remaining -ne '0') { throw 'Тимчасову базу відновлення не прибрано.' }
$before = (Invoke-StoreFlowPostgres -Settings $settings -Tool psql -Arguments @('--dbname','postgres','-X','-A','-t','-c',"SELECT coalesce(string_agg(datname,',' ORDER BY datname),'') FROM pg_database WHERE datname LIKE 'storeflow_restore_qa_%';") | Out-String).Trim()
$mismatchRejected = $false
try { $null = & (Join-Path $PSScriptRoot 'Test-DatabaseBackup.ps1') -BackupPath $backup.Path -ExpectedFingerprint ('0' * 32) }
catch { $mismatchRejected = $_.Exception.Message -match 'відрізняються' }
if (-not $mismatchRejected) { throw 'Не виявлено невідповідність даних.' }
$after = (Invoke-StoreFlowPostgres -Settings $settings -Tool psql -Arguments @('--dbname','postgres','-X','-A','-t','-c',"SELECT coalesce(string_agg(datname,',' ORDER BY datname),'') FROM pg_database WHERE datname LIKE 'storeflow_restore_qa_%';") | Out-String).Trim()
if ($before -ne $after) { throw 'Після невдалої перевірки залишилася тестова база.' }
$sourceAfter = (Invoke-StoreFlowPostgres -Settings $settings -Tool psql -Arguments @('--dbname',$SourceDatabase,'-X','-A','-t','-c',(Get-StoreFlowFingerprintSql)) | Out-String).Trim()
if ($sourceAfter -ne $expected) { throw 'Джерело резервної копії змінилося під час перевірки.' }
Write-Output 'PASS: nonempty backup restored; fingerprints and SHA256 match; mismatch rejected; temporary databases removed on success and failure; source database unchanged.'
