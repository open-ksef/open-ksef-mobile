#Requires -Version 5.1
<#
.SYNOPSIS
    Runs Mobile E2E tests with comprehensive preflight checks.
.DESCRIPTION
    Single-command runner for Android MAUI E2E tests via dotnet test + Appium.
    Validates all prerequisites, auto-fixes what it can, then executes tests.

    Prerequisites: Backend stack (open-ksef) running + ngrok HTTPS tunnel,
    Android emulator, app installed, Appium server.
    Start backend: cd C:\GIT2\open-ksef && ./scripts/dev-env-up.ps1
.PARAMETER Filter
    NUnit test filter expression. Default: "Category=Smoke|Category=Login".
    Examples:
      -Filter "Category=Login"
      -Filter "Category=Regression"
      -Filter "Category=Smoke|Category=Login|Category=Onboarding"
      -Filter "FullyQualifiedName~LoginFlowTests"
.PARAMETER SkipPreflightFix
    Only check prerequisites, don't auto-fix (no Appium auto-start).
.PARAMETER Verbosity
    dotnet test verbosity. Default: normal.
#>
[CmdletBinding()]
param(
    [string]$Filter = "Category=Smoke|Category=Login",
    [switch]$SkipPreflightFix,
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "normal"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$script:prefligthPassed = $true

function Write-Check {
    param([string]$Label, [bool]$Ok, [string]$Detail = "")
    if ($Ok) {
        Write-Host "  [OK] $Label" -ForegroundColor Green
        if ($Detail) { Write-Host "       $Detail" -ForegroundColor DarkGray }
    } else {
        Write-Host "  [FAIL] $Label" -ForegroundColor Red
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor Yellow }
        $script:prefligthPassed = $false
    }
}

# ── Load .env.test defaults ──────────────────────────────────────────
$envTestFile = Join-Path $root '.env.test'
$testEnv = @{}
if (Test-Path $envTestFile) {
    Get-Content $envTestFile | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+?)\s*$') {
            $testEnv[$Matches[1]] = $Matches[2]
        }
    }
}

Write-Host "`n========== Mobile E2E Preflight Checks ==========" -ForegroundColor Cyan

# ── 1. Docker containers ─────────────────────────────────────────────
Write-Host "`n[docker]" -ForegroundColor Cyan
$requiredContainers = @("openksef-postgres", "openksef-keycloak", "openksef-api", "openksef-gateway")
$runningContainers = @()
try {
    $runningContainers = @(docker ps --format "{{.Names}}" 2>&1)
} catch { }

foreach ($container in $requiredContainers) {
    $running = $runningContainers -contains $container
    Write-Check $container $running $(if (-not $running) { "Run dev-env-up.ps1 from the open-ksef backend repo" })
}

# ── 2. ngrok HTTPS tunnel ────────────────────────────────────────────
Write-Host "`n[ngrok]" -ForegroundColor Cyan
$ngrokUrl = $null
$ngrokApiPorts = @(4040, 4041, 4042)
foreach ($port in $ngrokApiPorts) {
    try {
        $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/tunnels" -TimeoutSec 3 -ErrorAction Stop
        $match = $tunnels.tunnels | Where-Object { $_.public_url -match '^https://' } | Select-Object -First 1
        if ($match) { $ngrokUrl = $match.public_url; break }
    } catch { }
}

$ngrokOk = $null -ne $ngrokUrl
Write-Check "ngrok HTTPS tunnel" $ngrokOk $(
    if ($ngrokOk) { $ngrokUrl }
    else { "No HTTPS tunnel found. Run: ngrok http 8080 (or dev-env-up.ps1 from the open-ksef backend repo)" }
)

if ($ngrokUrl -and $ngrokUrl -notmatch '^https://') {
    Write-Check "ngrok URL is HTTPS" $false "URL '$ngrokUrl' is not HTTPS. Android OIDC requires HTTPS."
}

# ── 3. ROPC login test ───────────────────────────────────────────────
Write-Host "`n[auth]" -ForegroundColor Cyan
$ropcOk = $false
if ($ngrokUrl) {
    try {
        $body = @{
            grant_type = "password"
            client_id  = "openksef-mobile"
            username   = ($testEnv['E2E_TEST_USER'] ?? "testuser")
            password   = ($testEnv['E2E_TEST_PASSWORD'] ?? "Test1234!")
        }
        $headers = @{ "ngrok-skip-browser-warning" = "true" }
        $tokenResp = Invoke-RestMethod `
            -Uri "$ngrokUrl/auth/realms/openksef/protocol/openid-connect/token" `
            -Method POST -Body $body -Headers $headers -TimeoutSec 15 -ErrorAction Stop
        $ropcOk = $null -ne $tokenResp.access_token
    } catch {
        $ropcError = $_.Exception.Message
    }
}
Write-Check "ROPC login via ngrok" $ropcOk $(
    if ($ropcOk) { "Token acquired for testuser" }
    elseif (-not $ngrokUrl) { "Skipped (no ngrok URL)" }
    else { "Failed: $ropcError" }
)

# ── 4. Android emulator ──────────────────────────────────────────────
Write-Host "`n[emulator]" -ForegroundColor Cyan
$androidHome = $env:ANDROID_HOME
if (-not $androidHome) {
    $sdkPaths = @("$env:LOCALAPPDATA\Android\Sdk", 'C:\Program Files (x86)\Android\android-sdk')
    $androidHome = $sdkPaths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if ($androidHome) { $env:ANDROID_HOME = $androidHome }

$adb = if ($androidHome) { Join-Path $androidHome 'platform-tools\adb.exe' } else { 'adb' }

$emulatorRunning = $false
try {
    $devices = & $adb devices 2>&1
    $emulatorRunning = ($devices | Select-String 'emulator-\d+\s+device') -ne $null
} catch { }
Write-Check "Android emulator" $emulatorRunning $(
    if (-not $emulatorRunning) { "No emulator device found. Start one from Android Studio or run setup-android-e2e.ps1" }
)

$appInstalled = $false
if ($emulatorRunning) {
    try {
        $pkgCheck = & $adb shell "pm list packages com.openksef.mobile" 2>&1
        $appInstalled = "$pkgCheck" -match 'com\.openksef\.mobile'
    } catch { }
}
Write-Check "App installed" $appInstalled $(
    if (-not $appInstalled -and $emulatorRunning) { "Run: dotnet build src/OpenKSeF.Mobile/OpenKSeF.Mobile.csproj -f net10.0-android -t:Install" }
)

# ── 5. Appium server ─────────────────────────────────────────────────
Write-Host "`n[appium]" -ForegroundColor Cyan
$appiumRunning = $false
try {
    $r = Invoke-WebRequest -Uri 'http://127.0.0.1:4723/status' -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
    if ($r.StatusCode -eq 200) { $appiumRunning = $true }
} catch { }

if (-not $appiumRunning -and -not $SkipPreflightFix) {
    Write-Host "  [fix] Starting Appium server ..." -ForegroundColor Yellow

    $env:JAVA_HOME = if (Test-Path "C:\Program Files\Java\jdk-25") { "C:\Program Files\Java\jdk-25" }
                     elseif ($env:JAVA_HOME) { $env:JAVA_HOME }
                     else { "" }

    $appiumCmd = (Get-Command appium -ErrorAction SilentlyContinue).Source
    if ($appiumCmd) {
        Start-Process powershell -ArgumentList "-NoProfile -WindowStyle Minimized -Command `"& '$appiumCmd' --address 127.0.0.1 --port 4723`"" -WindowStyle Hidden
        Start-Sleep -Seconds 10

        try {
            Invoke-WebRequest -Uri 'http://127.0.0.1:4723/status' -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop | Out-Null
            $appiumRunning = $true
        } catch { }
    }
}

Write-Check "Appium server (:4723)" $appiumRunning $(
    if (-not $appiumRunning) { "Run: appium --address 127.0.0.1 --port 4723" }
)

# ── 6. Set environment variables ─────────────────────────────────────
Write-Host "`n[env]" -ForegroundColor Cyan
if (-not $env:KEYCLOAK_USERNAME) { $env:KEYCLOAK_USERNAME = $testEnv['E2E_TEST_USER'] ?? "testuser" }
if (-not $env:KEYCLOAK_PASSWORD) { $env:KEYCLOAK_PASSWORD = $testEnv['E2E_TEST_PASSWORD'] ?? "Test1234!" }
if (-not $env:E2E_TEST_KSEF_TOKEN -and $testEnv['E2E_TEST_KSEF_TOKEN']) {
    $env:E2E_TEST_KSEF_TOKEN = $testEnv['E2E_TEST_KSEF_TOKEN']
}
Write-Host "  KEYCLOAK_USERNAME  = $($env:KEYCLOAK_USERNAME)" -ForegroundColor DarkGray
Write-Host "  KEYCLOAK_PASSWORD  = ****" -ForegroundColor DarkGray
Write-Host "  ANDROID_HOME       = $($env:ANDROID_HOME)" -ForegroundColor DarkGray

# ── Summary ───────────────────────────────────────────────────────────
Write-Host "`n=================================================" -ForegroundColor Cyan

if (-not $script:prefligthPassed) {
    Write-Host "`n  PREFLIGHT FAILED -- fix the issues above before running tests." -ForegroundColor Red
    Write-Host "  Quick fix: start backend via dev-env-up.ps1 (open-ksef repo) then ./scripts/setup-android-e2e.ps1`n" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n  All checks passed. Running tests ..." -ForegroundColor Green

# ── Run tests ─────────────────────────────────────────────────────────
$testProject = Join-Path $root 'src\OpenKSeF.Mobile.E2E.Android\OpenKSeF.Mobile.E2E.Android.csproj'

$testArgs = @(
    'test', $testProject,
    '--filter', $Filter,
    '--verbosity', $Verbosity,
    '--logger', 'trx;LogFileName=mobile-e2e-results.trx',
    '--nologo'
)

Write-Host "`n  dotnet $($testArgs -join ' ')`n" -ForegroundColor DarkGray

& dotnet @testArgs
$testExit = $LASTEXITCODE

if ($testExit -eq 0) {
    Write-Host "`n  Mobile E2E tests PASSED" -ForegroundColor Green
} else {
    Write-Host "`n  Mobile E2E tests FAILED (exit code $testExit)" -ForegroundColor Red
}

exit $testExit
