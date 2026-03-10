#Requires -Version 7.0
<#
.SYNOPSIS
    Prepares Android E2E testing: verifies prerequisites, starts Appium,
    builds/installs the MAUI app, and outputs the server URL for the emulator.
.DESCRIPTION
    Android emulator cannot reach host via "localhost" -- it uses 10.0.2.2.
    OIDC (WebAuthenticator) on Android requires HTTPS.
    Therefore this script requires an active ngrok tunnel and:
    1. Verifies ANDROID_HOME / adb / emulator
    2. Starts the Android emulator if not running
    3. Installs/updates Appium + UiAutomator2 driver
    4. Starts Appium server if not running
    5. Builds and installs the MAUI app on the emulator
    6. Prints the HTTPS server URL the app should use
.PARAMETER SkipBuild
    Skip MAUI app build (use previously built APK).
.PARAMETER SkipAppiumInstall
    Skip Appium/UiAutomator2 install (already installed).
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipAppiumInstall
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot

# ── 1. Detect ngrok URL ──────────────────────────────────────────────
Write-Host "[ngrok] Detecting HTTPS tunnel ..." -ForegroundColor Cyan

$ngrokUrl = $null
$ngrokApiPorts = @(4040, 4041, 4042)
foreach ($port in $ngrokApiPorts) {
    try {
        $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/tunnels" -TimeoutSec 3 -ErrorAction Stop
        $match = $tunnels.tunnels | Where-Object {
            $_.public_url -match '^https://'
        } | Select-Object -First 1
        if ($match) { $ngrokUrl = $match.public_url; break }
    } catch { }
}

if (-not $ngrokUrl) {
    Write-Error @"
No ngrok HTTPS tunnel found.
Android emulator requires HTTPS for OIDC login.
Run 'dev-env-up.ps1' from the open-ksef backend repo first (it starts ngrok automatically),
or start ngrok manually: ngrok http 8080
"@
}
Write-Host "  HTTPS URL: $ngrokUrl" -ForegroundColor Green

# ── 2. Verify Android SDK ────────────────────────────────────────────
Write-Host "`n[android] Checking SDK ..." -ForegroundColor Cyan

$sdkPaths = @(
    $env:ANDROID_HOME,
    "$env:LOCALAPPDATA\Android\Sdk",
    'C:\Program Files (x86)\Android\android-sdk'
)
$androidHome = $sdkPaths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $androidHome) {
    Write-Error "Android SDK not found. Install Android Studio or set ANDROID_HOME."
}

$env:ANDROID_HOME = $androidHome
$env:PATH = "$androidHome\platform-tools;$androidHome\emulator;$env:PATH"
Write-Host "  ANDROID_HOME = $androidHome" -ForegroundColor Green

$adb = Join-Path $androidHome 'platform-tools\adb.exe'
if (-not (Test-Path $adb)) { Write-Error "adb not found at $adb" }

# ── 3. Start emulator if not running ─────────────────────────────────
Write-Host "`n[emulator] Checking ..." -ForegroundColor Cyan

$devices = & adb devices 2>&1
$emulatorRunning = $devices | Select-String 'emulator-\d+\s+device'

if ($emulatorRunning) {
    Write-Host "  [OK] Emulator already running" -ForegroundColor Green
} else {
    $avds = & "$androidHome\emulator\emulator.exe" -list-avds 2>&1
    $avd = ($avds | Where-Object { $_ -match '\S' } | Select-Object -First 1).Trim()
    if (-not $avd) { Write-Error "No AVD found. Create one in Android Studio." }

    Write-Host "  Starting emulator '$avd' ..." -ForegroundColor Yellow
    Start-Process -FilePath "$androidHome\emulator\emulator.exe" `
        -ArgumentList "-avd $avd -no-snapshot-load" -WindowStyle Minimized

    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        $bootDone = & adb shell getprop sys.boot_completed 2>&1
        if ($bootDone.Trim() -eq '1') { break }
        Start-Sleep -Seconds 3
    }

    $bootCheck = & adb shell getprop sys.boot_completed 2>&1
    if ($bootCheck.Trim() -eq '1') {
        Write-Host "  [OK] Emulator booted" -ForegroundColor Green
    } else {
        Write-Error "Emulator failed to boot within 120s"
    }
}

# ── 4. Appium + UiAutomator2 ─────────────────────────────────────────
if (-not $SkipAppiumInstall) {
    Write-Host "`n[appium] Installing/updating ..." -ForegroundColor Cyan
    & npm install -g appium 2>&1 | Out-Null
    & appium driver install uiautomator2 2>&1 | Out-Null
    & appium driver update uiautomator2 2>&1 | Out-Null
    Write-Host "  [OK] Appium + UiAutomator2 ready" -ForegroundColor Green
}

# ── 5. Start Appium server if not running ─────────────────────────────
Write-Host "`n[appium] Checking server ..." -ForegroundColor Cyan

$appiumRunning = $false
try {
    $r = Invoke-WebRequest -Uri 'http://127.0.0.1:4723/status' -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
    if ($r.StatusCode -eq 200) { $appiumRunning = $true }
} catch { }

if ($appiumRunning) {
    Write-Host "  [OK] Appium server already running on :4723" -ForegroundColor Green
} else {
    Write-Host "  Starting Appium server ..." -ForegroundColor Yellow
    $appiumCmd = (Get-Command appium -ErrorAction SilentlyContinue).Source
    if (-not $appiumCmd) { Write-Error "appium not found in PATH. Run: npm install -g appium" }
    Start-Process powershell -ArgumentList "-NoProfile -WindowStyle Minimized -Command `"& '$appiumCmd' --address 127.0.0.1 --port 4723`"" -WindowStyle Hidden
    Start-Sleep -Seconds 8

    try {
        Invoke-WebRequest -Uri 'http://127.0.0.1:4723/status' -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop | Out-Null
        Write-Host "  [OK] Appium server started on :4723" -ForegroundColor Green
    } catch {
        Write-Warning "Appium may not have started. Check manually: appium --address 127.0.0.1 --port 4723"
    }
}

# ── 6. Build and install MAUI app ─────────────────────────────────────
$mauiProject = Join-Path $root 'src\OpenKSeF.Mobile\OpenKSeF.Mobile.csproj'

if (-not $SkipBuild) {
    Write-Host "`n[build] Building MAUI app for Android ..." -ForegroundColor Cyan
    & dotnet build $mauiProject -f net10.0-android -t:Install --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "MAUI build/install failed" }
    Write-Host "  [OK] App installed on emulator" -ForegroundColor Green
} else {
    Write-Host "`n[build] Skipped (-SkipBuild)" -ForegroundColor Yellow
}

# ── 7. Summary ────────────────────────────────────────────────────────
Write-Host "`n" -NoNewline
Write-Host ('=' * 65) -ForegroundColor DarkGray
Write-Host " Android E2E Environment Ready" -ForegroundColor Green
Write-Host ('=' * 65) -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Appium server       : http://127.0.0.1:4723"
Write-Host "  ANDROID_HOME        : $androidHome"
Write-Host "  App package         : com.openksef.mobile"
Write-Host ""
Write-Host "  Server URL for app  : $ngrokUrl" -ForegroundColor Yellow
Write-Host "  (Enter this URL on the login page of the mobile app)"
Write-Host ""
Write-Host "  Why ngrok?" -ForegroundColor DarkGray
Write-Host "    Android emulator cannot reach host via localhost." -ForegroundColor DarkGray
Write-Host "    It uses 10.0.2.2, but OIDC requires HTTPS." -ForegroundColor DarkGray
Write-Host "    ngrok provides an HTTPS URL accessible from emulator." -ForegroundColor DarkGray
Write-Host ""
Write-Host ('=' * 65) -ForegroundColor DarkGray
