param([string]$SourceDatabase)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Database-Tools.ps1')
$settings = Get-StoreFlowDatabase $SourceDatabase
$directory = Join-Path (Split-Path $PSScriptRoot -Parent) 'backups'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$path = Join-Path $directory ('storeflow-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N') + '.dump')
Invoke-StoreFlowPostgres -Settings $settings -Tool pg_dump -Arguments @('--dbname',$settings.Database,'--format','custom','--file',$path)
$restore = Find-PostgresTool pg_restore
$null = & $restore --list $path
if ($LASTEXITCODE -ne 0 -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Архів не пройшов перевірку формату.' }
[pscustomobject]@{ Path = $path; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
