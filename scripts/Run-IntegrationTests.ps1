param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Для перевірок інтерфейсу потрібен Node.js 20 або новіший.' }
& (Join-Path $PSScriptRoot 'Test-PublicConfiguration.ps1')
node --test (Join-Path $PSScriptRoot 'frontend-tests.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Перевірки інтерфейсу виявили помилки.' }
dotnet build (Join-Path $projectRoot 'StoreFlow.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Не вдалося зібрати рішення.' }
dotnet test (Join-Path $projectRoot 'StoreFlow.UnitTests') -c $Configuration --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Unit-тести виявили помилки.' }
dotnet run --project (Join-Path $projectRoot 'StoreFlow.IntegrationTests') -c $Configuration --no-build -- $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Інтеграційні перевірки виявили помилки.' }
