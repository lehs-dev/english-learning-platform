param(
    [string]$BaseUrl = 'http://127.0.0.1:18080',
    [string]$RunName = ('duong-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskUri = [Uri]$BaseUrl
if ($taskUri.Host -notin @('127.0.0.1', 'localhost') -or $taskUri.Scheme -notin @('http','https')) { throw 'Use a private local demo URL.' }
if ($RunName -notmatch '^[a-zA-Z0-9-]+$') { throw 'RunName must contain letters, numbers and hyphens only.' }
if ($env:ELP_STUDENT_UAT_ISOLATED -ne 'true') { throw 'Confirm the dedicated database with ELP_STUDENT_UAT_ISOLATED=true first.' }
if (-not $env:LearningDemo__Password) { throw 'Supply LearningDemo__Password through environment, never command arguments.' }
$taskOutput = Join-Path $taskRoot "TestResults/duong-e2e/$RunName"
if (Test-Path $taskOutput) { throw 'Use a new RunName to preserve previous evidence.' }
$null = New-Item -ItemType Directory -Path $taskOutput
$taskPreviousUrl = $env:ELP_UAT_URL
$taskPreviousResults = $env:ELP_UAT_RESULTS
$taskCases = @()
$taskStart = [DateTimeOffset]::UtcNow
Push-Location $taskRoot
try {
    $taskHead = (& git rev-parse HEAD).Trim()
    $env:ELP_UAT_URL = $BaseUrl
    foreach ($taskGroup in @('auth','learning','practice','assessment','checkout','responsive','roles','admin')) {
        $env:ELP_UAT_RESULTS = Join-Path $taskOutput $taskGroup
        if ($taskGroup -eq 'roles') { & node tests/browser/handover-roles-uat.mjs }
        elseif ($taskGroup -eq 'admin') { & node tests/browser/admin-uat.mjs }
        else { & node tests/browser/student-flow-uat.mjs --group $taskGroup }
        $taskCases += [PSCustomObject]@{ Group=$taskGroup; Passed=($LASTEXITCODE -eq 0); ReportDirectory=$taskGroup }
    }
    $taskReport = [ordered]@{ BaseCommit=$taskHead; WorkingTreeClean=(-not [bool](& git status --porcelain));
        StartedAtUtc=$taskStart.ToString('O'); FinishedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');
        Timezone='Asia/Saigon'; BaseUrl=$BaseUrl; Groups=$taskCases;
        Note='Local automated E2E, not GitHub CI or a signed human rehearsal. Review sourceBefore/sourceAfter digests in Student reports.' }
    $taskReport | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskOutput 'run.json') -Encoding utf8
    Get-ChildItem $taskOutput -Recurse -File | Sort-Object FullName | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.FullName.Substring($taskOutput.Length + 1).Replace('\','/')
    } | Set-Content (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "Evidence: $taskOutput"
    if ($taskCases.Passed -contains $false) { throw 'One or more groups failed. Read their reports; do not mark the run passed.' }
} finally {
    $env:ELP_UAT_URL=$taskPreviousUrl
    $env:ELP_UAT_RESULTS=$taskPreviousResults
    Pop-Location
}
