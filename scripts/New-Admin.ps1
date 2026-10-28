param([string]$Email)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'StoreFlow.Аpi'
if ([string]::IsNullOrWhiteSpace($Email)) { $Email = Read-Host 'Email першого адміністратора' }
$password = Read-Host 'Пароль адміністратора (щонайменше 12 символів)' -AsSecureString
$previousEmail = $env:BootstrapAdmin__Email
$previousPassword = $env:BootstrapAdmin__Password
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:BootstrapAdmin__Email = $Email
    $env:BootstrapAdmin__Password = [Net.NetworkCredential]::new('', $password).Password
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    dotnet run --project $projectPath --no-build -- --bootstrap-admin
    if ($LASTEXITCODE -ne 0) { throw 'Адміністратора не створено. Перевірте повідомлення вище.' }
}
finally {
    $env:BootstrapAdmin__Email = $previousEmail
    $env:BootstrapAdmin__Password = $previousPassword
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $password.Dispose()
}
