param([ValidatePattern('^[a-zA-Z0-9-]+$')][string]$BundleName = 'handover-20261021')
$ErrorActionPreference = 'Stop'
$taskPackRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskPackOutput = Join-Path $taskPackRoot "TestResults/qa6/$BundleName"
if (Test-Path -LiteralPath $taskPackOutput) { throw 'Choose another BundleName; earlier evidence must not be overwritten.' }
if (Test-Path -LiteralPath "$taskPackOutput.zip") { throw 'Archive already exists.' }

# Chỉ lấy bằng chứng đã chọn. Không lấy .env, web log, cookie, storage state hay database.
$taskPackFiles = @(
    'docs/acceptance/practice-assessment-a1-checklist.md',
    'docs/acceptance/qa2-scoring-results.md', 'docs/acceptance/qa3-timeout-resume-results.md',
    'docs/acceptance/qa4-owner-enrollment-results.md', 'docs/acceptance/qa5-journey-results.md',
    'docs/acceptance/qa6-handover-results.md', 'docs/handover/qa6-run-demo.md',
    'docs/architecture/stimulus-audio-history.md',
    'scripts/qa6-start-demo.ps1', 'scripts/qa6-demo-smoke.mjs', 'scripts/qa6-package-evidence.ps1',
    'TestResults/qa2/qa2-unit.trx', 'TestResults/qa2/qa2-integration.trx',
    'TestResults/qa2/qa2-local-retry.trx', 'TestResults/qa2/qa2-integration-docker.trx',
    'TestResults/qa3/qa3-unit.trx', 'TestResults/qa3/qa3-regression.trx', 'TestResults/qa3/qa3-browser.xml',
    'TestResults/qa4/qa4-confirmed-red.trx', 'TestResults/qa4/qa4-regression-verified.trx',
    'TestResults/qa4/qa4-unit-verified.trx',
    'TestResults/qa5/qa5-practice-preview.trx', 'TestResults/qa5/qa5-regression.trx', 'TestResults/qa5/qa5-unit.trx',
    'TestResults/qa6/demo-smoke.json'
)
foreach ($taskPackRun in 1..6) {
    $taskPackFiles += ('TestResults/qa5/browser-run-{0:d2}/qa5-results.json' -f $taskPackRun)
}
$taskPackFiles += Get-ChildItem (Join-Path $taskPackRoot 'TestResults/qa5/browser-run-06') -Filter '*.png' |
    ForEach-Object { 'TestResults/qa5/browser-run-06/' + $_.Name }
foreach ($taskPackStage in 'qa2', 'qa3', 'qa4', 'qa5', 'qa6') {
    $taskPackFiles += "TestResults/$taskPackStage/security-audit.txt"
    $taskPackFiles += "TestResults/$taskPackStage/security-audit.csv"
}

# Chặn mẫu secret thường gặp trước khi copy; đây không phải chứng nhận mọi secret detector.
foreach ($taskPackFile in $taskPackFiles) {
    $taskPackPath = Join-Path $taskPackRoot $taskPackFile
    if (-not (Test-Path -LiteralPath $taskPackPath -PathType Leaf)) { throw "Missing evidence: $taskPackFile" }
    if ([IO.Path]::GetExtension($taskPackPath) -ne '.png') {
        $taskPackText = Get-Content -LiteralPath $taskPackPath -Raw
        $taskPackPattern = '(?i)(Password\s*=|Set-Cookie\s*:|"access_token"\s*:|"cookies"\s*:)'
        if ([IO.Path]::GetExtension($taskPackPath) -in @('.ps1', '.mjs')) {
            # Source tham chiếu env không phải mật khẩu literal. Vẫn chặn literal được gán.
            # Source còn được quét riêng bằng security-audit.py, không bỏ qua toàn bộ script.
            $taskPackPattern = '(?i)(Password\s*=\s*["''][^"'']+|Set-Cookie\s*:|"access_token"\s*:|"cookies"\s*:)'
        }
        if ($taskPackText -match $taskPackPattern) {
            throw "Potential secret in $taskPackFile; review locally before sharing."
        }
    }
}
New-Item -ItemType Directory -Path $taskPackOutput | Out-Null
$taskPackManifest = @()
$taskPackCases = @()
foreach ($taskPackFile in $taskPackFiles) {
    $taskPackSource = Join-Path $taskPackRoot $taskPackFile
    $taskPackTarget = Join-Path $taskPackOutput $taskPackFile
    New-Item -ItemType Directory -Path (Split-Path $taskPackTarget) -Force | Out-Null
    Copy-Item -LiteralPath $taskPackSource -Destination $taskPackTarget
    $taskPackManifest += [PSCustomObject]@{ Path = $taskPackFile; SHA256 = (Get-FileHash $taskPackTarget -Algorithm SHA256).Hash }
    if ($taskPackFile.EndsWith('.trx')) {
        [xml]$taskPackTrx = Get-Content -LiteralPath $taskPackSource -Raw
        foreach ($taskPackResult in $taskPackTrx.TestRun.Results.UnitTestResult) {
            $taskPackCases += [PSCustomObject]@{
                Artifact = $taskPackFile; Test = [string]$taskPackResult.testName
                Actual = [string]$taskPackResult.outcome; Duration = [string]$taskPackResult.duration
            }
        }
    }
}
# CSV actual theo từng lượt chạy: cùng test có thể xuất hiện nhiều lần, không cộng thành tổng duy nhất.
$taskPackCases | Export-Csv (Join-Path $taskPackOutput 'testcase-actual.csv') -NoTypeInformation -Encoding utf8
$taskPackManifest += [PSCustomObject]@{ Path = 'testcase-actual.csv'; SHA256 = (Get-FileHash (Join-Path $taskPackOutput 'testcase-actual.csv')).Hash }
$taskPackManifest | Export-Csv (Join-Path $taskPackOutput 'manifest-sha256.csv') -NoTypeInformation -Encoding utf8
Compress-Archive -Path (Join-Path $taskPackOutput '*') -DestinationPath "$taskPackOutput.zip"
Write-Output "Evidence archive: $taskPackOutput.zip"
Write-Output ('Archive SHA256: ' + (Get-FileHash "$taskPackOutput.zip").Hash)
Write-Output 'Keep this archive with the reviewed source revision; it is not included by git push.'
