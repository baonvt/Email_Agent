#Requires -Version 7.0
# Optional helper: generates .env and configures the API's local User Secrets.
$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path $PSScriptRoot -Parent
$envPath = Join-Path $workspacePath '.env'

if (Test-Path -LiteralPath $envPath) {
    Write-Host '.env already exists; preserved it. Configure local API User Secrets as described in README.md.'
    exit 0
}

function New-LocalSecret {
    $bytes = [byte[]]::new(32)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

$postgresSecret = New-LocalSecret
$redisSecret = New-LocalSecret
$jwtSecret = New-LocalSecret
$contents = @"
POSTGRES_PASSWORD=$postgresSecret
REDIS_PASSWORD=$redisSecret
JWT_SIGNING_KEY=$jwtSecret
POSTGRES_PORT=5432
REDIS_PORT=6379
FRONTEND_ORIGIN=http://localhost:3000
SENDGRID_DELIVERY_ENABLED=false
SENDGRID_API_KEY=
SENDGRID_FROM_EMAIL=
"@
[System.IO.File]::WriteAllText($envPath, $contents)
$apiPath = Join-Path $workspacePath 'Project_AI/Project_AI.API.csproj'
dotnet user-secrets set 'ConnectionStrings:Postgres' "Host=127.0.0.1;Port=5432;Database=inboxagent;Username=inboxagent;Password=$postgresSecret" --project $apiPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not configure PostgreSQL User Secrets.' }
dotnet user-secrets set 'ConnectionStrings:Redis' "127.0.0.1:6379,password=$redisSecret" --project $apiPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Redis User Secrets.' }
dotnet user-secrets set 'Jwt:SigningKey' $jwtSecret --project $apiPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not configure JWT User Secrets.' }
Write-Host 'Generated .env and local API User Secrets. No SendGrid credentials have been set.'
