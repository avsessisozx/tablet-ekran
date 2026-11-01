# TabletEkran Android APK derleme betigi.
# Gerekli ortam degiskenleri:
#   JAVA_HOME      : JDK 17+ (javac, keytool, jarsigner)
#   ANDROID_HOME   : Android SDK kok dizini
# Istege bagli:
#   ANDROID_PLATFORM : platforms/android-XX (varsayilan: en yuksek android.jar)
#
# Imzalama icin her derlemede build\ altinda gecici bir debug anahtari uretilir;
# anahtar ve sifresi depoya girmez, islem sonunda silinir.

param(
    [string]$AndroidSdk = $env:ANDROID_HOME,
    [string]$Jdk = $env:JAVA_HOME,
    [string]$Platform = $env:ANDROID_PLATFORM
)

$ErrorActionPreference = 'Stop'

if (-not $AndroidSdk) {
    if ($env:ANDROID_SDK_ROOT) { $AndroidSdk = $env:ANDROID_SDK_ROOT }
    else { throw 'ANDROID_HOME (veya ANDROID_SDK_ROOT) tanimli degil.' }
}
if (-not $Jdk) { throw 'JAVA_HOME tanimli degil.' }

$proj = $PSScriptRoot
$build = Join-Path $proj 'build'
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $build 'classes') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $build 'dex') -Force | Out-Null

$bt = Get-ChildItem (Join-Path $AndroidSdk 'build-tools') -Directory |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $bt) { throw 'Android build-tools bulunamadi.' }
$platformKok = Join-Path $AndroidSdk 'platforms'
if (-not $Platform) {
    $adaylar = @(Get-ChildItem $platformKok -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'android.jar') } |
        Sort-Object Name -Descending)
    if ($adaylar.Count -eq 0) { throw 'Android platform (android.jar) bulunamadi.' }
    $Platform = $adaylar[0].FullName
}
$androidJar = Join-Path $Platform 'android.jar'
if (-not (Test-Path $androidJar)) { throw "android.jar yok: $androidJar" }
Write-Host ("SDK: " + $AndroidSdk)
Write-Host ("Platform: " + $Platform)
Write-Host ("Build tools: " + $bt.FullName)

$env:JAVA_HOME = $Jdk
$env:Path = (Join-Path $Jdk 'bin') + ';' + $bt.FullName + ';' + $env:Path

Write-Host '[1/7] aapt2 compile (res)'
$resZip = Join-Path $build 'res.zip'
& (Join-Path $bt.FullName 'aapt2.exe') compile --dir (Join-Path $proj 'res') -o $resZip
if ($LASTEXITCODE -ne 0) { throw 'aapt2 compile basarisiz' }

Write-Host '[2/7] aapt2 link (manifest + resources)'
$baseApk = Join-Path $build 'base.apk'
& (Join-Path $bt.FullName 'aapt2.exe') link -o $baseApk `
    -I $androidJar `
    --manifest (Join-Path $proj 'AndroidManifest.xml') `
    --min-sdk-version 16 --target-sdk-version 16 `
    --version-code 3 --version-name '0.3' `
    --auto-add-overlay `
    -R $resZip
if ($LASTEXITCODE -ne 0) { throw 'aapt2 link basarisiz' }

Write-Host '[3/7] javac'
$kaynaklar = Get-ChildItem (Join-Path $proj 'src') -Recurse -Filter *.java |
    ForEach-Object { $_.FullName }
& (Join-Path $Jdk 'bin\javac.exe') -encoding UTF-8 -source 1.8 -target 1.8 `
    -classpath $androidJar -d (Join-Path $build 'classes') $kaynaklar
if ($LASTEXITCODE -ne 0) { throw 'javac basarisiz' }

Write-Host '[4/7] d8'
$siniflar = Get-ChildItem (Join-Path $build 'classes') -Recurse -Filter *.class |
    ForEach-Object { $_.FullName }
& (Join-Path $bt.FullName 'd8.bat') --release --min-api 16 --lib $androidJar `
    --output (Join-Path $build 'dex') $siniflar
if ($LASTEXITCODE -ne 0) { throw 'd8 basarisiz' }

Write-Host '[5/7] dex + zipalign'
Push-Location (Join-Path $build 'dex')
& (Join-Path $bt.FullName 'aapt.exe') add $baseApk classes.dex
$dexKodu = $LASTEXITCODE
Pop-Location
if ($dexKodu -ne 0) { throw 'classes.dex eklenemedi' }
$aligned = Join-Path $build 'aligned.apk'
& (Join-Path $bt.FullName 'zipalign.exe') -f 4 $baseApk $aligned
if ($LASTEXITCODE -ne 0) { throw 'zipalign basarisiz' }

Write-Host '[6/7] gecici debug anahtari ile imzalama'
$ks = Join-Path $build 'debug.keystore'
$sifre = [Guid]::NewGuid().ToString('N')
& (Join-Path $Jdk 'bin\keytool.exe') -genkeypair -keystore $ks `
    -storepass $sifre -keypass $sifre -alias androiddebugkey `
    -keyalg RSA -keysize 2048 -validity 10000 `
    -dname 'CN=Android Debug,O=Android,C=US' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'gecici anahtar olusturulamadi' }

$apk = Join-Path $build 'TabletEkran-0.3.apk'
& (Join-Path $bt.FullName 'apksigner.bat') sign --ks $ks `
    --ks-pass ("pass:" + $sifre) --key-pass ("pass:" + $sifre) `
    --ks-key-alias androiddebugkey --min-sdk-version 16 --out $apk $aligned
if ($LASTEXITCODE -ne 0) { throw 'apksigner basarisiz' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($apk)
try {
    $girdi = $zip.Entries | Where-Object { $_.FullName -eq 'META-INF/MANIFEST.MF' }
    if ($girdi -ne $null) {
        $okuyucu = New-Object System.IO.StreamReader($girdi.Open())
        $manifest = $okuyucu.ReadToEnd()
        $okuyucu.Close()
        if ($manifest -match 'SHA-256-Digest') {
            $gecici = Join-Path $build 'signed-sha1.apk'
            & (Join-Path $Jdk 'bin\jarsigner.exe') -keystore $ks `
                -storepass $sifre -keypass $sifre `
                -sigalg SHA1withRSA -digestalg SHA1 `
                -signedjar $gecici $aligned androiddebugkey | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'jarsigner basarisiz' }
            Remove-Item $apk -Force
            & (Join-Path $bt.FullName 'zipalign.exe') -f 4 $gecici $apk
            if ($LASTEXITCODE -ne 0) { throw 'imza sonrasi zipalign basarisiz' }
            Remove-Item $gecici -Force
        }
    }
} finally {
    $zip.Dispose()
}

Write-Host '[7/7] dogrulama'
& (Join-Path $bt.FullName 'apksigner.bat') verify --min-sdk-version 16 --verbose $apk
if ($LASTEXITCODE -ne 0) { throw 'apksigner verify basarisiz' }

# Gecici anahtar depoya/calisma agacina kalmaz.
Remove-Item $ks -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host ("APK hazir: " + $apk)
Write-Host ("Boyut    : " + [math]::Round((Get-Item $apk).Length / 1024, 1) + " KB")
