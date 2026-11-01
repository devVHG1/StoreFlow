param([switch]$Docker)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Database-Tools.ps1')
$data=Get-Content (Join-Path $PSScriptRoot 'demo-catalog.json') -Raw | ConvertFrom-Json
function QuoteSql([string]$value){ "U&'"+(($value.ToCharArray() | ForEach-Object { '\'+([int]$_).ToString('X4') }) -join '')+"'" }
$parts=[Collections.Generic.List[string]]::new()
$parts.Add('BEGIN; SELECT pg_advisory_xact_lock(1946101101);')
foreach($category in $data.categories){
 $name=QuoteSql $category.name; $description=QuoteSql $category.description
 $parts.Add("INSERT INTO `"Categories`" (`"Name`",`"Description`") SELECT $name,$description WHERE NOT EXISTS (SELECT 1 FROM `"Categories`" WHERE `"Name`"=$name);")
}
foreach($product in $data.products){
 $name=QuoteSql $product.name; $description=QuoteSql $product.description; $category=QuoteSql $product.category
 $price=([decimal]$product.price).ToString([Globalization.CultureInfo]::InvariantCulture); $quantity=[int]$product.stockQuantity
 $parts.Add("INSERT INTO `"Products`" (`"Name`",`"Description`",`"Price`",`"StockQuantity`",`"CategoryId`") SELECT $name,$description,$price,$quantity,(SELECT min(`"Id`") FROM `"Categories`" WHERE `"Name`"=$category) WHERE NOT EXISTS (SELECT 1 FROM `"Products`" WHERE `"Name`"=$name);")
}
$parts.Add('COMMIT;')
$sql=$parts -join "`n"
if($Docker){
 Push-Location (Split-Path $PSScriptRoot -Parent)
 try { $sql | docker compose exec -T db sh -c 'exec psql --no-password -X --set=ON_ERROR_STOP=1 --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"'; if($LASTEXITCODE -ne 0){throw 'Не вдалося додати демонстраційні товари.'} } finally { Pop-Location }
}else{
 $settings=Get-StoreFlowDatabase
 Invoke-StoreFlowPostgres $settings psql @('--dbname',$settings.Database,'-X','--set=ON_ERROR_STOP=1','-c',$sql)
}
Write-Output 'Демонстраційний каталог додано. Наявні назви, ціни й залишки не змінені. Повторний запуск не створює дублікатів.'


