$ErrorActionPreference = 'Stop'

function Get-StoreFlowDatabase {
    param([string]$SourceDatabase)
    $root = Split-Path $PSScriptRoot -Parent
    $connection = $env:ConnectionStrings__DefaultConnection
    if ([string]::IsNullOrWhiteSpace($connection)) {
        $project = [xml](Get-Content -LiteralPath (Join-Path $root 'StoreFlow.Аpi/StoreFlow.Аpi.csproj') -Raw)
        $secretId = [string]($project.Project.PropertyGroup.UserSecretsId | Where-Object { $_ })
        $secretRoot = if ($IsWindows) { Join-Path $env:APPDATA 'Microsoft/UserSecrets' } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.microsoft/usersecrets' }
        $secretPath = Join-Path $secretRoot "$secretId/secrets.json"
        if (Test-Path -LiteralPath $secretPath) {
            $settings = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json -AsHashtable
            $connection = $settings['ConnectionStrings:DefaultConnection']
        }
    }
    if ([string]::IsNullOrWhiteSpace($connection)) { throw 'Налаштуйте User Secrets або ConnectionStrings__DefaultConnection.' }
    $parts = [Data.Common.DbConnectionStringBuilder]::new()
    $parts.set_ConnectionString($connection)
    function Field([string[]]$Names, [string]$Fallback = '') {
        foreach ($name in $Names) { if ($parts.ContainsKey($name)) { return [string]$parts.get_Item($name) } }
        return $Fallback
    }
    $database = if ($SourceDatabase) { $SourceDatabase } else { Field @('Database','Initial Catalog') }
    if ($database -notmatch '^[A-Za-z0-9_][A-Za-z0-9_-]{0,62}$') { throw 'Для цих локальних інструментів назва бази має містити лише латинські літери, цифри, _ або -.' }
    [pscustomobject]@{
        HostName = Field @('Host','Server') 'localhost'
        Port = Field @('Port') '5432'
        UserName = Field @('Username','User ID','UserName') 'postgres'
        Password = Field @('Password','Pwd')
        Database = $database
        SslMode = (Field @('SSL Mode')).Replace('VerifyFull','verify-full').Replace('VerifyCA','verify-ca').ToLowerInvariant()
    }
    $parts.Clear()
}

function Find-PostgresTool([string]$Name) {
    if (-not $IsWindows) {
        $version17 = "/usr/lib/postgresql/17/bin/$Name"
        if (Test-Path -LiteralPath $version17) { return $version17 }
    }
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    if ($IsWindows) {
        $base = Join-Path $env:ProgramFiles 'PostgreSQL'
        if (Test-Path -LiteralPath $base) {
            foreach ($version in (Get-ChildItem -LiteralPath $base -Directory | Sort-Object { try { [version]$_.Name } catch { [version]"0.0" } } -Descending)) {
                $candidate = Join-Path $version.FullName "bin/$Name.exe"
                if (Test-Path -LiteralPath $candidate) { return $candidate }
            }
        }
    }
    throw "Не знайдено $Name. Установіть клієнтські інструменти PostgreSQL сумісної версії та додайте їх у PATH."
}

function Invoke-StoreFlowPostgres {
    param($Settings, [string]$Tool, [string[]]$Arguments)
    $previousPassword = $env:PGPASSWORD
    $previousSsl = $env:PGSSLMODE
    $previousTimeout = $env:PGCONNECT_TIMEOUT
    $previousOptions = $env:PGOPTIONS
    try {
        $env:PGPASSWORD = $Settings.Password
        if ($Settings.SslMode) { $env:PGSSLMODE = $Settings.SslMode }
        $env:PGCONNECT_TIMEOUT = '5'
        $env:PGOPTIONS = ($previousOptions + ' -c timezone=UTC').Trim()
        $executable = Find-PostgresTool $Tool
        & $executable --host $Settings.HostName --port $Settings.Port --username $Settings.UserName --no-password @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Операція $Tool завершилася помилкою. Основна база не замінювалася." }
    }
    finally {
        $env:PGPASSWORD = $previousPassword
        $env:PGSSLMODE = $previousSsl
        $env:PGCONNECT_TIMEOUT = $previousTimeout
        $env:PGOPTIONS = $previousOptions
    }
}

function Get-StoreFlowFingerprintSql {
    $tables = @('Categories','Products','Users','Orders','OrderItems','StockMovements','OrderStatusEvents','OrderRequests','__EFMigrationsHistory')
    $pieces = foreach ($table in $tables) {
        $key = if ($table -eq '__EFMigrationsHistory') { 'MigrationId' } else { 'Id' }
        "COALESCE((SELECT md5(string_agg(md5(to_jsonb(t)::text), '' ORDER BY t.`"$key`")) FROM `"$table`" t),'empty')"
    }
    'SELECT md5(' + ($pieces -join ' || ') + ');'
}
