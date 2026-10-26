# Builds a signed gpsTrainer release .aab on Windows.
# Passwords are never printed. They are read from environment variables or,
# when this script creates the upload key, written to a secrets file outside the repo.
#
# Required once the keystore exists:
#   AndroidSigningKeyStore   path to the .jks upload keystore
#   AndroidSigningKeyAlias   key alias
#   AndroidSigningStorePass  keystore password
#   AndroidSigningKeyPass    key password
# The GPSTRAINER_ANDROID_* names are accepted as well.
#
# If AndroidSigningKeyStore is unset, the keystore is
# %USERPROFILE%\.gpstrainer\gpstrainer-upload.jks.
# If that file does not exist, this script creates it and writes
# %USERPROFILE%\.gpstrainer\secrets.txt. Keep that keystore. Play rejects an
# update signed with a different upload key.

$ErrorActionPreference = "Stop"

function Get-EnvValue([string]$Name, [string]$FallbackName) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable($FallbackName)
    }
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return $value
}

function New-Secret([int]$Length = 32) {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $text = [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "A").Replace("/", "B")
    if ($text.Length -lt $Length) { return $text }
    return $text.Substring(0, $Length)
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $PSScriptRoot "GpsTrainer.App.csproj"
$alias = Get-EnvValue "AndroidSigningKeyAlias" "GPSTRAINER_ANDROID_KEY_ALIAS"
if ([string]::IsNullOrWhiteSpace($alias)) { $alias = "gpstrainer-upload" }

$keystore = Get-EnvValue "AndroidSigningKeyStore" "GPSTRAINER_ANDROID_KEYSTORE"
if ([string]::IsNullOrWhiteSpace($keystore)) {
    $keystore = Join-Path $env:USERPROFILE ".gpstrainer\gpstrainer-upload.jks"
}

$secretDir = Split-Path -Parent $keystore
New-Item -ItemType Directory -Force -Path $secretDir | Out-Null

$storePass = Get-EnvValue "AndroidSigningStorePass" "GPSTRAINER_ANDROID_STORE_PASS"
$keyPass = Get-EnvValue "AndroidSigningKeyPass" "GPSTRAINER_ANDROID_KEY_PASS"

if (-not (Test-Path -LiteralPath $keystore)) {
    if ([string]::IsNullOrWhiteSpace($storePass)) { $storePass = New-Secret }
    # PKCS12 uses one password. A different key password makes jarsigner reject the key.
    $keyPass = $storePass
    $keytool = Get-Command keytool -ErrorAction SilentlyContinue
    if (-not $keytool) {
        $candidates = @(
            (Join-Path $env:JAVA_HOME "bin\keytool.exe"),
            "$env:ProgramFiles\Android\Android Studio\jbr\bin\keytool.exe",
            "$env:LocalAppData\Programs\Android\Android Studio\jbr\bin\keytool.exe"
        )
        $keytoolPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
        if (-not $keytoolPath) { throw "keytool was not found. Install a JDK and put it on PATH." }
    } else {
        $keytoolPath = $keytool.Source
    }

    $env:GPSTRAINER_STORE_PASS = $storePass
    $env:GPSTRAINER_KEY_PASS = $keyPass
    & $keytoolPath -genkeypair -keystore $keystore -alias $alias -keyalg RSA -keysize 2048 -validity 10000 -storepass:env GPSTRAINER_STORE_PASS -keypass:env GPSTRAINER_KEY_PASS -dname "CN=gpsTrainer, OU=Mobile, O=Peak Software Consulting LLC, C=US" -noprompt
    if ($LASTEXITCODE -ne 0) { throw "keytool failed while creating the upload keystore." }
    Remove-Item Env:GPSTRAINER_STORE_PASS -ErrorAction SilentlyContinue
    Remove-Item Env:GPSTRAINER_KEY_PASS -ErrorAction SilentlyContinue

    $note = Join-Path $secretDir "secrets.txt"
    @(
        "gpsTrainer Play upload key"
        "keystore: $keystore"
        "alias: $alias"
        "store password: $storePass"
        "key password: $keyPass"
        "Do not commit this file. Do not paste these values into git, a pull request, or chat."
    ) | Set-Content -LiteralPath $note -Encoding utf8
    Write-Host "Created upload keystore. Passwords are only in: $note"
} else {
    if ([string]::IsNullOrWhiteSpace($storePass) -or [string]::IsNullOrWhiteSpace($keyPass)) {
        throw "The keystore already exists. Set AndroidSigningStorePass and AndroidSigningKeyPass. This script does not print them."
    }
    Write-Host "Using existing upload keystore: $keystore"
}

$env:AndroidSigningKeyStore = $keystore
$env:AndroidSigningKeyAlias = $alias
$env:AndroidSigningStorePass = $storePass
$env:AndroidSigningKeyPass = $keyPass

Push-Location $repoRoot
try {
    dotnet publish $project -f net10.0-android -c Release -r android-arm64 -p:AndroidPackageFormat=aab
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
} finally {
    Pop-Location
    Remove-Item Env:AndroidSigningStorePass -ErrorAction SilentlyContinue
    Remove-Item Env:AndroidSigningKeyPass -ErrorAction SilentlyContinue
}

$bundles = Get-ChildItem -Path (Join-Path $PSScriptRoot "bin\Release") -Filter *.aab -Recurse | Sort-Object LastWriteTime -Descending
if (-not $bundles) { throw "Publish finished without an .aab." }
Write-Host "AAB: $($bundles[0].FullName)"
