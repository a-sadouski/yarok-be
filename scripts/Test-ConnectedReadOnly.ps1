param([string]$Origin = 'http://localhost:3000', [string]$EnvFile = "$PSScriptRoot/../../yarok/.env", [switch]$ApiOnly)
$ErrorActionPreference = 'Stop'
$settings = @{}
foreach ($line in Get-Content -LiteralPath $EnvFile) {
    if ($line -match '^\s*(ADMIN_LOGIN|ADMIN_PASSWORD)=(.*)$') {
        $value = $Matches[2].Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) { $value = $value.Substring(1, $value.Length - 2) }
        $settings[$Matches[1]] = $value
    }
}
$products = Invoke-RestMethod "$Origin/api/products"
foreach ($product in $products) {
    foreach ($field in @('id','categoryId','name','slug','description','isVariable','isActive','createdAt','imagePath','previewImage','variants')) {
        if ($product.PSObject.Properties.Name -notcontains $field) { throw "Missing product field: $field" }
    }
    foreach ($variant in $product.variants) {
        if ($variant.price -isnot [string]) { throw 'Price must retain Drizzle string representation' }
    }
}
Write-Output "Products: $(@($products).Count), response shape verified"
foreach ($route in @('categories','colors')) { $rows = Invoke-RestMethod "$Origin/api/$route"; Write-Output "${route}: $(@($rows).Count)" }
$body = @{ login = $settings['ADMIN_LOGIN']; password = $settings['ADMIN_PASSWORD'] } | ConvertTo-Json -Compress
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$null = Invoke-RestMethod "$Origin/api/admin/login" -Method Post -ContentType application/json -Body $body -WebSession $session
$me = Invoke-RestMethod "$Origin/api/admin/me" -WebSession $session
if (!$me.authenticated) { throw 'Proxy did not forward auth cookie' }
if (!$ApiOnly) {
    $page = Invoke-WebRequest "$Origin/admin/products" -WebSession $session -MaximumRedirection 0 -SkipHttpErrorCheck
    if ([int]$page.StatusCode -ne 200) { throw 'Admin SSR page did not preserve login' }
}
$null = Invoke-RestMethod "$Origin/api/admin/logout" -Method Post -WebSession $session
$me = Invoke-WebRequest "$Origin/api/admin/me" -WebSession $session -SkipHttpErrorCheck
if ([int]$me.StatusCode -ne 401) { throw 'Logout did not clear proxy cookie' }
if ($ApiOnly) { Write-Output 'PASS: ASP.NET Core -> PostgreSQL, response contract, login cookie, logout. No database writes or emails.' }
else { Write-Output 'PASS: Nuxt proxy -> ASP.NET Core -> PostgreSQL, response contract, login cookie, authenticated SSR, logout. No database writes or emails.' }
