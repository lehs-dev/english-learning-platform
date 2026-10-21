param(
    [ValidatePattern('^elp-qa6-[a-z0-9-]+$')]
    [string]$ProjectName = ('elp-qa6-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateRange(1024, 65535)][int]$WebPort = 18080,
    [ValidateRange(1024, 65535)][int]$SqlPort = 15433
)
$ErrorActionPreference = 'Stop'
$taskDemoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# Chỉ dựng project mới. Không reset database hoặc đổi mật khẩu của demo đang có.
& docker info --format '{{.ServerVersion}}' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Open Docker Desktop and wait for Engine readiness.' }
$taskDemoExisting = @(& docker ps -aq --filter "label=com.docker.compose.project=$ProjectName")
$taskDemoVolumes = @(& docker volume ls -q --filter "label=com.docker.compose.project=$ProjectName")
if ($taskDemoExisting.Count -or $taskDemoVolumes.Count) { throw 'Project already has resources. Use another name; nothing was changed.' }
if ($WebPort -eq $SqlPort) { throw 'WebPort and SqlPort must differ.' }
foreach ($taskDemoPort in @($WebPort, $SqlPort)) {
    if (Get-NetTCPConnection -State Listen -LocalPort $taskDemoPort -ErrorAction SilentlyContinue) {
        throw "Port $taskDemoPort is occupied. No existing service will be stopped."
    }
}

# Mật khẩu nhập kín, không đưa literal vào lịch sử lệnh, file hoặc báo cáo.
# CI có thể truyền environment; các giá trị cũ được khôi phục sau script.
$taskDemoValues = @{
    ELP_WEB_PORT = [string]$WebPort; ELP_SQL_PORT = [string]$SqlPort
    ELP_IMAGE_TAG = $ProjectName; ELP_CANDIDATE_SHA = 'development'
    ASPNETCORE_ENVIRONMENT = 'Development'; LearningDemo__Enabled = 'true'
    Payments__Enabled = 'false'; CheckoutSandbox__Enabled = 'false'
    MSSQL_SA_PASSWORD = $env:MSSQL_SA_PASSWORD
    LearningDemo__Password = $env:LearningDemo__Password
}
foreach ($taskDemoKey in @('MSSQL_SA_PASSWORD', 'LearningDemo__Password')) {
    if ([string]::IsNullOrWhiteSpace($taskDemoValues[$taskDemoKey])) {
        $taskDemoSecure = Read-Host "Enter $taskDemoKey (12+ chars, upper/lower/digit/symbol)" -AsSecureString
        $taskDemoValues[$taskDemoKey] = [Net.NetworkCredential]::new('', $taskDemoSecure).Password
    }
    $taskDemoValue = $taskDemoValues[$taskDemoKey]
    if ($taskDemoValue.Length -lt 12 -or $taskDemoValue -cnotmatch '[A-Z]' -or
        $taskDemoValue -cnotmatch '[a-z]' -or $taskDemoValue -notmatch '[0-9]' -or
        $taskDemoValue -notmatch '[^a-zA-Z0-9]') { throw 'Use a stronger password before creating demo resources.' }
    # SQL connection string hiện được ghép từ environment trong Compose.
    if ($taskDemoValue.Contains(';') -or $taskDemoValue.Contains('"') -or $taskDemoValue.Contains("`n") -or $taskDemoValue.Contains("`r")) {
        throw 'For this Compose demo, passwords must not contain semicolon, quote or newline.'
    }
}
$taskDemoPrevious = @{}
try {
    foreach ($taskDemoKey in $taskDemoValues.Keys) {
        $taskDemoPrevious[$taskDemoKey] = [Environment]::GetEnvironmentVariable($taskDemoKey, 'Process')
        [Environment]::SetEnvironmentVariable($taskDemoKey, $taskDemoValues[$taskDemoKey], 'Process')
    }
    Push-Location $taskDemoRoot
    try {
        # Dùng example không chứa secret, không đọc .env riêng của người đang làm việc.
        & docker compose --env-file .env.example --project-name $ProjectName up --build --detach
        if ($LASTEXITCODE -ne 0) { throw 'Demo startup failed. Keep resources for diagnosis; do not delete volumes.' }
    } finally { Pop-Location }
    $taskDemoReady = $false
    for ($taskDemoRetry = 0; $taskDemoRetry -lt 60; $taskDemoRetry++) {
        try {
            $taskDemoResponse = Invoke-WebRequest "http://127.0.0.1:$WebPort/health" -TimeoutSec 2
            if ($taskDemoResponse.StatusCode -eq 200) { $taskDemoReady = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $taskDemoReady) { throw 'Demo is not healthy yet. Inspect local web logs without sharing secrets.' }
    Write-Output "Demo ready: http://localhost:$WebPort ; project=$ProjectName ; SQL port=$SqlPort"
    Write-Output 'Use teacher1/student1@demo.example.test with your entered demo password.'
    Write-Output 'Database and audio volumes are retained. See docs/handover/qa6-run-demo.md.'
} finally {
    foreach ($taskDemoKey in $taskDemoPrevious.Keys) {
        [Environment]::SetEnvironmentVariable($taskDemoKey, $taskDemoPrevious[$taskDemoKey], 'Process')
    }
    $taskDemoValues.Clear()
    $taskDemoValue = $null
    $taskDemoSecure = $null
}
