param(
    [string]$HostName = 'localhost',
    [int]$Port = 5432,
    [string]$Database = 'storeflow',
    [string]$UserName = 'postgres'
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'StoreFlow.Аpi\StoreFlow.Аpi.csproj'
$password = Read-Host 'Пароль PostgreSQL' -AsSecureString
$parts = [System.Data.Common.DbConnectionStringBuilder]::new()
try {
    $parts.set_Item('Host', $HostName)
    $parts.set_Item('Port', $Port)
    $parts.set_Item('Database', $Database)
    $parts.set_Item('Username', $UserName)
    $parts.set_Item('Password', [Net.NetworkCredential]::new('', $password).Password)
    dotnet user-secrets set 'ConnectionStrings:DefaultConnection' $parts.get_ConnectionString() --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Не вдалося зберегти підключення PostgreSQL.' }
    Write-Output 'Підключення збережено в локальних секретах поза проєктом.'
}
finally {
    $parts.Clear()
    $password.Dispose()
}
