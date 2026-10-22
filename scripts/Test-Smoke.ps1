param([string]$ApiDll = "$PSScriptRoot/../src/Yarok.Api/bin/Debug/net10.0/Yarok.Api.dll")
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:15050'
$env:ADMIN_LOGIN = 'smoke-admin'
$env:ADMIN_PASSWORD = 'smoke-password'
$env:ADMIN_SESSION_SECRET = 'smoke-only-session-secret'
$env:DATABASE_URL = 'Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1'
$env:SUPABASE_URL = 'https://storage.invalid'
$env:SUPABASE_SERVICE_KEY = 'smoke-only-key'
$env:SMTP_HOST = 'smtp.invalid'
$env:SMTP_USER = 'smoke@example.invalid'
$env:SMTP_PASSWORD = 'smoke-only-password'
$env:CONTACT_TO_EMAIL = 'receiver@example.invalid'
$null = New-Item -ItemType Directory -Force (Join-Path $root '.local')
$out = Join-Path $root '.local/smoke.stdout.log'
$err = Join-Path $root '.local/smoke.stderr.log'
$process = Start-Process dotnet -ArgumentList @($ApiDll) -PassThru -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err
function Check($method, $path, $body, $expected, $session = $null, $contentType = 'application/json') {
    $options = @{ Uri = "http://127.0.0.1:15050$path"; Method = $method; SkipHttpErrorCheck = $true }
    if ($null -ne $body) { $options.Body = $body; $options.ContentType = $contentType }
    if ($null -ne $session) { $options.WebSession = $session }
    $response = Invoke-WebRequest @options
    if ([int]$response.StatusCode -ne $expected) { throw "$method $path expected $expected, got $($response.StatusCode): $($response.Content)" }
    return $response
}
try {
    $ready = $false
    for ($i=0; $i -lt 40; $i++) {
        try { $null = Check GET /health $null 200; $ready = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw 'API did not start' }
    $null = Check GET /api/admin/me $null 401
    $null = Check POST /api/products/create '{}' 401
    $null = Check PUT /api/products/1 '{}' 401
    $null = Check POST /api/admin/login '{"login":"wrong","password":"wrong"}' 401
    $null = Check POST /api/admin/login 'not-json' 415 $null 'text/plain'
    $null = Check POST /api/contact 'not-json' 415 $null 'text/plain'
    $null = Check POST /api/contact '{' 400
    $session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $login = Check POST /api/admin/login '{"login":"smoke-admin","password":"smoke-password"}' 200 $session
    if ($login.Headers['Set-Cookie'] -notmatch 'httponly') { throw 'Session must be HttpOnly' }
    $null = Check GET /api/admin/me $null 200 $session
    $null = Check PUT /api/products/nope '{}' 400 $session
    $null = Check POST /api/products/create 'name=x' 400 $session 'application/x-www-form-urlencoded'
    $null = Check POST /api/products/create 'name=x&slug=x&categoryId=1&price=-1&colorId=1' 400 $session 'application/x-www-form-urlencoded'
    $null = Check POST /api/products/create 'name=x&slug=x&categoryId=1&price=1&colorId=1&isActive=invalid' 400 $session 'application/x-www-form-urlencoded'
    $null = Check PUT /api/products/1 'name=x&slug=x&categoryId=1&price=1&colorId=invalid' 400 $session 'application/x-www-form-urlencoded'
    $null = Check POST /api/products/create 'name=x&slug=x&categoryId=1&isVariable=true&variants=invalid' 400 $session 'application/x-www-form-urlencoded'
    $null = Check POST /api/products/create 'name=x&slug=x&categoryId=1&isVariable=true&variants=%5B%7B%22colorId%22%3A1%2C%22price%22%3A%2210%22%7D%2C%7B%22colorId%22%3A1%2C%22price%22%3A%2220%22%7D%5D' 400 $session 'application/x-www-form-urlencoded'
    $null = Check POST /api/contact '{}' 400
    $null = Check POST /api/contact '{"name":"test"}' 400
    $null = Check POST /api/contact '{"name":"test","phone":"123"}' 400
    $null = Check POST /api/contact '{"company":"bot"}' 200
    $null = Check POST /api/admin/logout '{}' 200 $session
    $null = Check GET /api/admin/me $null 401 $session
    Write-Output 'PASS: health, login/logout, cookie, authorization, JSON/content-type errors, contact honeypot, required fields, product price/variants/boolean validation (24 checks).'
} finally { if (!$process.HasExited) { Stop-Process -Id $process.Id } }
