param([string]$RunName = ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
$ErrorActionPreference = 'Stop'
$taskQa5Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($RunName -notmatch '^[a-zA-Z0-9-]+$') { throw 'RunName may only contain letters, numbers and hyphens.' }
$taskQa5Output = Join-Path $taskQa5Root ('TestResults/qa5/' + $RunName)
if (Test-Path -LiteralPath $taskQa5Output) { throw 'Choose a new RunName to preserve earlier evidence.' }
New-Item -ItemType Directory -Path $taskQa5Output | Out-Null
$taskQa5Audio = Join-Path ([IO.Path]::GetTempPath()) ('ELP_QA5_Audio_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskQa5Audio | Out-Null
$taskQa5Container = 'elp-qa5-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$taskQa5Web = $null
$taskQa5SqlStarted = $false
try {
    foreach ($taskQa5Port in @(15055, 18085)) {
        if (Get-NetTCPConnection -State Listen -LocalPort $taskQa5Port -ErrorAction SilentlyContinue) {
            throw "UAT port $taskQa5Port is already occupied; no existing service will be stopped."
        }
    }
    # Password chỉ nằm trong environment của process này và các child cần dùng.
    # Không ghi .env, không truyền literal vào shell history hoặc report.
    $taskQa5SqlPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'aA1!'
    $env:MSSQL_SA_PASSWORD = $taskQa5SqlPassword
    $taskQa5DemoPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'aA1!'
    $env:LearningDemo__Password = $taskQa5DemoPassword
    & docker run --detach --name $taskQa5Container --label elp.qa5=isolated --memory 2g `
        --publish '127.0.0.1:15055:1433' --env ACCEPT_EULA=Y --env MSSQL_SA_PASSWORD `
        --env MSSQL_MEMORY_LIMIT_MB=1024 mcr.microsoft.com/mssql/server:2022-latest | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot start private SQL Server container.' }
    $taskQa5SqlStarted = $true
    Write-Output 'Private SQL Server started; waiting for readiness.'
    $taskQa5Ready = $false
    for ($taskQa5Retry = 0; $taskQa5Retry -lt 45; $taskQa5Retry++) {
        # Chỉ health check SELECT 1; không tạo/sửa dữ liệu nghiệp vụ bằng SQL.
        & docker exec $taskQa5Container sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "SELECT 1" >/dev/null 2>&1' 2>$null
        if ($LASTEXITCODE -eq 0) { $taskQa5Ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $taskQa5Ready) { throw 'Private SQL Server did not become ready.' }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:18085'
    $env:ConnectionStrings__DefaultConnection = "Server=127.0.0.1,15055;Database=ELP_QA5;User Id=sa;Password=$taskQa5SqlPassword;TrustServerCertificate=True;MultipleActiveResultSets=true"
    $env:Database__ApplyMigrationsOnStartup = 'true'
    $env:LearningDemo__Enabled = 'true'
    $env:StimulusAudio__StoragePath = $taskQa5Audio
    $env:Payments__Enabled = 'false'
    $env:CheckoutSandbox__Enabled = 'false'
    $env:Logging__LogLevel__Default = 'Warning'
    $taskQa5Dll = Join-Path $taskQa5Root 'src/EnglishLearningPlatform.Web/bin/Release/net10.0/EnglishLearningPlatform.Web.dll'
    if (-not (Test-Path -LiteralPath $taskQa5Dll)) { throw 'Run dotnet build --configuration Release first.' }
    $taskQa5Web = Start-Process -FilePath 'dotnet' -ArgumentList @($taskQa5Dll, '--contentRoot', (Join-Path $taskQa5Root 'src/EnglishLearningPlatform.Web')) `
        -WorkingDirectory $taskQa5Root -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $taskQa5Output 'web.stdout.log') -RedirectStandardError (Join-Path $taskQa5Output 'web.stderr.log')
    $taskQa5Ready = $false
    for ($taskQa5Retry = 0; $taskQa5Retry -lt 60; $taskQa5Retry++) {
        if ($taskQa5Web.HasExited) { throw 'Private web exited; inspect sanitized local logs.' }
        try {
            $taskQa5Health = Invoke-WebRequest 'http://127.0.0.1:18085/health' -TimeoutSec 2
            if ($taskQa5Health.StatusCode -eq 200) { $taskQa5Ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $taskQa5Ready) { throw 'Private web did not become healthy.' }
    # Node chỉ cần password demo, không cần connection string hoặc password SQL.
    Remove-Item Env:ConnectionStrings__DefaultConnection,Env:MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue
    $env:ELP_QA5_UAT_ISOLATED = 'true'
    $env:ELP_UAT_URL = 'http://127.0.0.1:18085'
    $env:ELP_UAT_RESULTS = $taskQa5Output
    Write-Output 'Private web healthy; starting live browser UAT.'
    Push-Location (Join-Path $taskQa5Root 'tests/browser')
    try { & node qa5-journey-uat.mjs; $taskQa5ExitCode = $LASTEXITCODE }
    finally { Pop-Location }
    if ($taskQa5ExitCode -ne 0) { throw 'QA5 browser UAT failed; inspect its redacted report and screenshots.' }
} finally {
    if ($taskQa5Web -and -not $taskQa5Web.HasExited) { Stop-Process -Id $taskQa5Web.Id -Force }
    # Chỉ xóa container do lượt này tạo, không dùng docker compose down hoặc prune.
    if ($taskQa5SqlStarted) { & docker rm --force --volumes $taskQa5Container | Out-Null }
    $taskQa5ResolvedAudio = [IO.Path]::GetFullPath($taskQa5Audio)
    $taskQa5TempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($taskQa5ResolvedAudio.StartsWith($taskQa5TempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($taskQa5ResolvedAudio) -match '^ELP_QA5_Audio_[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $taskQa5ResolvedAudio -Recurse -Force
    }
    Write-Output "QA5 disposable app/SQL/audio cleaned. Evidence retained at $taskQa5Output"
}
