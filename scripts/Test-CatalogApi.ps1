param([string]$BaseUrl = 'http://localhost:5276', [string]$AccessToken = $env:STOREFLOW_ACCESS_TOKEN)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AccessToken)) { throw 'Задайте STOREFLOW_ACCESS_TOKEN із токеном Admin або Manager.' }
$checks = 0
function Call-Api($Method, $Path, $Body, $Expected) {
    $params = @{ Method = $Method; Uri = "$BaseUrl/api/$Path"; SkipHttpErrorCheck = $true; TimeoutSec = 20; Headers = @{ Authorization = "Bearer $AccessToken" } }
    if ($Method -eq 'PUT' -and $Path -match '^products/\d+$' -and $Body) {
        $current = Invoke-RestMethod -Uri "$BaseUrl/api/$Path" -Headers $params.Headers -SkipHttpErrorCheck
        if ($null -ne $current.version) { $Body.version = $current.version; $Body.stockChangeReason = "QA stock adjustment" }
    }
    if ($null -ne $Body) { $params.ContentType = 'application/json'; $params.Body = ($Body | ConvertTo-Json) }
    $response = Invoke-WebRequest @params
    $content = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
    if ([int]$response.StatusCode -ne $Expected) { throw "$Method $Path : $($response.StatusCode), expected $Expected. $content" }
    $script:checks++
    if ($content) { return ($content | ConvertFrom-Json) }
}
$category = $null
$product = $null
try {
    $category = Call-Api POST categories @{name="API test $([guid]::NewGuid())"; description='test'} 201
    $null = Call-Api GET categories $null 200
    $null = Call-Api GET "categories/$($category.id)" $null 200
    $null = Call-Api PUT "categories/$($category.id)" @{name='Updated test';description='updated'} 204
    $updated = Call-Api GET "categories/$($category.id)" $null 200
    if ($updated.name -ne 'Updated test') { throw 'Category update not persisted' }
    $product = Call-Api POST products @{name='Test product';price=12.50;stockQuantity=3;categoryId=$category.id} 201
    $null = Call-Api GET products $null 200
    $null = Call-Api GET "products/$($product.id)" $null 200
    $null = Call-Api PUT "products/$($product.id)" @{name='Updated product';description='updated';price=15;stockQuantity=4;categoryId=$category.id} 204
    $updated = Call-Api GET "products/$($product.id)" $null 200
    if ($updated.name -ne 'Updated product' -or $updated.price -ne 15 -or $updated.stockQuantity -ne 4) { throw 'Product update not persisted' }
    $null = Call-Api DELETE "categories/$($category.id)" $null 409
    $null = Call-Api POST categories @{name='   '} 400
    $null = Call-Api POST categories @{name=''} 400
    $null = Call-Api POST products @{name='Invalid';price=-1;stockQuantity=-1;categoryId=$category.id} 400
    $null = Call-Api POST products @{name='Invalid';price=1;stockQuantity=0;categoryId=2147483647} 400
    $null = Call-Api GET categories/2147483647 $null 404
    $null = Call-Api GET products/2147483647 $null 404
    $null = Call-Api PUT products/2147483647 @{name='Missing';price=1;stockQuantity=0;categoryId=$category.id} 404
    $null = Call-Api DELETE "products/$($product.id)" $null 204
    $null = Call-Api GET "products/$($product.id)" $null 404
    $product = $null
    $null = Call-Api DELETE "categories/$($category.id)" $null 204
    $null = Call-Api GET "categories/$($category.id)" $null 404
    $category = $null
    Write-Output "PASS: $checks HTTP checks; updates persisted; temporary records removed."
}
finally {
    if ($product) { $null = Call-Api DELETE "products/$($product.id)" $null 204 }
    if ($category) { $null = Call-Api DELETE "categories/$($category.id)" $null 204 }
}
