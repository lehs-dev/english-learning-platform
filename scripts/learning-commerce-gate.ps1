param(
    [string]$OutputDirectory = 'artifacts/learning-commerce-week6/final',
    [int]$WebPort = 5086,
    [switch]$SkipRegression,
    [switch]$SkipBrowser
)

# Windows gate: an ephemeral SQL Server 2022 container is shared by the existing
# GUID test fixtures and a separate fresh UAT database. No application DB is used.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
foreach ($tool in @('dotnet', 'docker', 'node', 'npm.cmd', 'git')) { Get-Command $tool -ErrorAction Stop | Out-Null }
$taskEnvironmentKeys = @('MSSQL_SA_PASSWORD', 'SQLCMDPASSWORD', 'ELP_TEST_SQLSERVER', 'ConnectionStrings__DefaultConnection',
    'LearningDemo__Enabled', 'LearningDemo__Password', 'Payments__Enabled', 'CheckoutSandbox__Enabled',
    'ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT', 'ELP_UAT_URL', 'ELP_UAT_RESULTS')
$taskPrevious = @{}
foreach ($key in $taskEnvironmentKeys) { $taskPrevious[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
$taskBytes = New-Object byte[] 32
$taskRandom = [Security.Cryptography.RandomNumberGenerator]::Create()
$taskRandom.GetBytes($taskBytes)
$taskRandom.Dispose()
$taskPassword = [Convert]::ToBase64String($taskBytes) + 'Aa1!'
$taskContainerId = $null
$taskWeb = $null
$taskStages = @()
$taskManifest = [ordered]@{
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    timezone = 'Asia/Saigon'
    branch = (git branch --show-current).Trim()
    candidateSha = (git rev-parse HEAD).Trim()
    workingTreeClean = -not [bool](git status --porcelain)
    sdk = (dotnet --version).Trim()
    databaseIsolation = 'Disposable SQL Server 2022 container; GUID test fixtures; fresh UAT database'
    outcome = 'Running'
}

function Invoke-Checked([string]$Name, [string]$Executable, [string[]]$Arguments) {
    $taskStageStart = [DateTimeOffset]::UtcNow.ToString('O')
    $taskLog = Join-Path $taskOutput ($Name + '.log')
    $taskPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Executable @Arguments 2>&1 | ForEach-Object {
            $taskLine = $_.ToString().Replace($taskPassword, '[REDACTED]')
            Add-Content -LiteralPath $taskLog -Value $taskLine -Encoding UTF8
            Write-Host $taskLine
        }
        $taskExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $taskPreference }
    $script:taskStages += [ordered]@{ name = $Name; startedAtUtc = $taskStageStart; exitCode = $taskExit }
    if ($taskExit -ne 0) { throw "$Name failed with exit code $taskExit. See its log." }
}

try {
    Invoke-Checked 'restore' 'dotnet' @('restore', 'EnglishLearningPlatform.sln')
    Invoke-Checked 'build' 'dotnet' @('build', 'EnglishLearningPlatform.sln', '-c', 'Release', '--no-restore')
    $taskManifest['webAssemblySha256'] = (Get-FileHash 'src/EnglishLearningPlatform.Web/bin/Release/net10.0/EnglishLearningPlatform.Web.dll').Hash.ToLowerInvariant()
    $taskManifest['testAssemblySha256'] = (Get-FileHash 'tests/EnglishLearningPlatform.IntegrationTests/bin/Release/net10.0/EnglishLearningPlatform.IntegrationTests.dll').Hash.ToLowerInvariant()
    $env:MSSQL_SA_PASSWORD = $taskPassword
    $env:SQLCMDPASSWORD = $taskPassword
    $taskContainerName = 'elp-learning-commerce-' + [Guid]::NewGuid().ToString('N')
    $taskManifest['sqlContainerName'] = $taskContainerName
    $taskDockerArguments = @('run', '--detach', '--name', $taskContainerName, '--label', 'elp.week6-owned=true',
        '--env', 'ACCEPT_EULA=Y', '--env', 'MSSQL_SA_PASSWORD', '--publish', '127.0.0.1::1433',
        'mcr.microsoft.com/mssql/server:2022-latest')
    $taskContainerId = (& docker @taskDockerArguments).Trim()
    if ($LASTEXITCODE -ne 0 -or $taskContainerId -notmatch '^[a-f0-9]{64}$') { throw 'Could not start the isolated SQL container.' }
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 90; $taskAttempt++) {
        try {
            & docker exec --env SQLCMDPASSWORD $taskContainerId /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q 'SELECT 1' 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $taskReady = $true; break }
        } catch { # The container may be starting its SQL/sqlcmd binaries. Retry readiness only.
        }
        Start-Sleep -Seconds 1
    }
    if (-not $taskReady) { throw 'The isolated SQL Server did not become ready.' }
    $taskSqlPort = (& docker port $taskContainerId '1433/tcp').Trim().Split(':')[-1]
    $taskManifest['sqlImageId'] = (& docker inspect --format '{{.Image}}' $taskContainerId).Trim()
    & docker exec --env SQLCMDPASSWORD $taskContainerId /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)) AS Version;" |
        Set-Content -LiteralPath (Join-Path $taskOutput 'sql-version.txt') -Encoding UTF8
    $env:ELP_TEST_SQLSERVER = "Server=127.0.0.1,$taskSqlPort;User Id=sa;Password=$taskPassword;TrustServerCertificate=True;MultipleActiveResultSets=true"
    Invoke-Checked 'journeys' 'dotnet' @('test', 'tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj',
        '-c', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~LearningCommerceJourneyTests',
        '--logger', 'trx;LogFileName=journeys.trx', '--results-directory', $taskOutput)
    if (-not $SkipRegression) {
        Invoke-Checked 'unit' 'dotnet' @('test', 'tests/EnglishLearningPlatform.UnitTests/EnglishLearningPlatform.UnitTests.csproj',
            '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=unit.trx', '--results-directory', $taskOutput)
        Invoke-Checked 'integration' 'dotnet' @('test', 'tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj',
            '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=integration.trx', '--results-directory', $taskOutput)
    }
    if (-not $SkipBrowser) {
        $taskDemoDatabase = 'EnglishLearningPlatformUat_' + [Guid]::NewGuid().ToString('N')
        $taskManifest['demoDatabase'] = $taskDemoDatabase
        $env:ConnectionStrings__DefaultConnection = $env:ELP_TEST_SQLSERVER + ";Database=$taskDemoDatabase"
        $env:LearningDemo__Enabled = 'false'
        $env:LearningDemo__Password = $taskPassword
        $env:Payments__Enabled = 'false'
        $env:CheckoutSandbox__Enabled = 'true'
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:DOTNET_ENVIRONMENT = 'Development'
        $taskDll = Join-Path $taskRoot 'src/EnglishLearningPlatform.Web/bin/Release/net10.0/EnglishLearningPlatform.Web.dll'
        Invoke-Checked 'fresh-migrations' 'dotnet' @($taskDll, '--migrate')
        $env:LearningDemo__Enabled = 'true'
        $env:ELP_UAT_URL = "http://127.0.0.1:$WebPort"
        $taskStartArguments = @{
            FilePath = 'dotnet'
            ArgumentList = @(('"' + $taskDll + '"'), '--urls', $env:ELP_UAT_URL)
            WorkingDirectory = Join-Path $taskRoot 'src/EnglishLearningPlatform.Web'
            WindowStyle = 'Hidden'
            PassThru = $true
            RedirectStandardOutput = Join-Path $taskOutput 'web.log'
            RedirectStandardError = Join-Path $taskOutput 'web-error.log'
        }
        $taskWeb = Start-Process @taskStartArguments
        $taskHealthy = $false
        for ($taskAttempt = 0; $taskAttempt -lt 90; $taskAttempt++) {
            if ($taskWeb.HasExited) { throw 'The demo web process exited; inspect its redacted logs.' }
            try {
                $taskResponse = Invoke-WebRequest -Uri ($env:ELP_UAT_URL + '/health') -UseBasicParsing -TimeoutSec 2
                if ($taskResponse.StatusCode -eq 200) { $taskHealthy = $true; break }
            } catch { Start-Sleep -Seconds 1 }
        }
        if (-not $taskHealthy) { throw 'The fresh demo web did not become healthy.' }
        $env:ELP_UAT_RESULTS = Join-Path $taskOutput 'browser-existing'
        Invoke-Checked 'browser-existing' 'node' @('tests/browser/uat.mjs')
        $env:ELP_UAT_RESULTS = Join-Path $taskOutput 'browser-learning-commerce'
        Invoke-Checked 'browser-learning-commerce' 'node' @('tests/browser/learning-commerce-uat.mjs')
        & docker cp 'scripts/learning-commerce-evidence.sql' "${taskContainerId}:/tmp/learning-commerce-evidence.sql" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not copy the read-only evidence query.' }
        $taskEvidenceArguments = @('exec', '--env', 'SQLCMDPASSWORD', $taskContainerId, '/opt/mssql-tools18/bin/sqlcmd',
            '-S', 'localhost', '-U', 'sa', '-C', '-b', '-d', $taskDemoDatabase, '-y', '0', '-w', '65535',
            '-i', '/tmp/learning-commerce-evidence.sql')
        $taskJson = (& docker @taskEvidenceArguments) -join ''
        if ($LASTEXITCODE -ne 0) { throw 'Could not read demo database evidence.' }
        # sqlcmd forbids -h -1 together with -y 0. Strip its column header and
        # separator, retaining the complete untruncated FOR JSON array.
        $taskJsonStart = $taskJson.IndexOf('[{')
        if ($taskJsonStart -lt 0) { throw 'No JSON evidence array returned by SQL Server.' }
        $taskJson = $taskJson.Substring($taskJsonStart).Trim()
        # Windows PowerShell 5 emits a JSON array as one pipeline object; an
        # outer @() would wrap that array again and incorrectly report count 1.
        $taskRows = $taskJson | ConvertFrom-Json
        $taskRows | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutput 'database-evidence.json') -Encoding UTF8
        if (@($taskRows).Count -ne 2) { throw 'Demo course count mismatch.' }
        foreach ($row in $taskRows) {
            if ($row.AppliedMigrations -ne 6 -or $row.DemoUsers -ne 5 -or $row.Enrollments -ne 1 -or
                $row.HistoricalCompletions -ne 1 -or $row.Status -ne 'Archived' -or $row.CompletedEffectiveLessons -ne 1) {
                throw 'Persisted demo learning data did not match the browser journey.'
            }
            if ($row.IsPaid -and ($row.Orders -ne 2 -or $row.FailedPayments -ne 1 -or $row.SuccessfulPayments -ne 1 -or
                $row.SuccessfulPaymentAmount -ne 199000 -or $row.EffectiveLessons -ne 3)) { throw 'Paid demo invariants failed.' }
            if (-not $row.IsPaid -and ($row.Orders -ne 0 -or $row.FailedPayments -ne 0 -or $row.SuccessfulPayments -ne 0 -or
                $row.SuccessfulPaymentAmount -ne 0 -or $row.EffectiveLessons -ne 2)) { throw 'Free demo invariants failed.' }
        }
    }
    $taskManifest['outcome'] = 'Passed'
} catch {
    $taskManifest['outcome'] = 'Failed'
    $taskManifest['error'] = $_.Exception.Message.Replace($taskPassword, '[REDACTED]')
    throw
} finally {
    if ($taskWeb -and -not $taskWeb.HasExited) { Stop-Process -Id $taskWeb.Id -Force }
    if ($taskContainerId -match '^[a-f0-9]{64}$') {
        try {
            $taskLabels = (& docker inspect --format '{{json .Config.Labels}}' $taskContainerId) | ConvertFrom-Json
            if ($taskLabels.'elp.week6-owned' -eq 'true') {
                & docker rm --force $taskContainerId | Out-Null
                $taskManifest['containerCleanupExitCode'] = $LASTEXITCODE
            }
        } catch { $taskManifest['containerCleanupError'] = $_.Exception.Message.Replace($taskPassword, '[REDACTED]') }
    }
    foreach ($taskLogPath in @('web.log', 'web-error.log')) {
        $taskLogFile = Join-Path $taskOutput $taskLogPath
        if (Test-Path -LiteralPath $taskLogFile) {
            $taskLogContent = [IO.File]::ReadAllText($taskLogFile).Replace($taskPassword, '[REDACTED]')
            [IO.File]::WriteAllText($taskLogFile, $taskLogContent, [Text.UTF8Encoding]::new($false))
        }
    }
    foreach ($key in $taskEnvironmentKeys) { [Environment]::SetEnvironmentVariable($key, $taskPrevious[$key], 'Process') }
    $taskManifest['stages'] = $taskStages
    $taskManifest['finishedAtUtc'] = [DateTimeOffset]::UtcNow.ToString('O')
    $taskManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskOutput 'run.json') -Encoding UTF8
    $taskPassword = $null
}
