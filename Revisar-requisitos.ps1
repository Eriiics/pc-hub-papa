# Revisa que este PC tenga lo necesario para armar el paquete de PC Hub y explica en español qué falta.
# Uso: clic derecho -> "Ejecutar con PowerShell" (o lo llama Crear-paquete.ps1 antes de compilar).
# Si todo está bien, deja JAVA_HOME apuntando a un JDK válido y crea android\local.properties si faltaba.
$raiz = $PSScriptRoot
$faltan = @()

function Ok($texto)  { Write-Host "  [OK] $texto" -ForegroundColor Green }
function Falta($texto, $solucion) {
    Write-Host "  [FALTA] $texto" -ForegroundColor Yellow
    Write-Host "          $solucion" -ForegroundColor Gray
    $script:faltan += $texto
}

Write-Host "Revisando lo necesario para armar PC Hub…" -ForegroundColor Cyan

# 1. .NET 8 o más nuevo (SDK, no solo el "runtime")
$sdkMayor = 0
try {
    $sdks = & dotnet --list-sdks 2>$null
    foreach ($l in $sdks) { if ($l -match '^(\d+)\.') { $sdkMayor = [math]::Max($sdkMayor, [int]$Matches[1]) } }
} catch { }
if ($sdkMayor -ge 8) { Ok ".NET SDK $sdkMayor" }
else { Falta "El SDK de .NET 8 (o más nuevo)" "Instálalo con: winget install Microsoft.DotNet.SDK.8   (o bájalo de https://dotnet.microsoft.com/download)" }

# 2. JDK 17 o 21 (con el JDK 25 falla Gradle)
function VersionJdk($carpeta) {
    $rel = Join-Path $carpeta 'release'
    if (Test-Path $rel) {
        $m = Select-String -Path $rel -Pattern '^JAVA_VERSION="?(\d+)' | Select-Object -First 1
        if ($m) { return [int]$m.Matches[0].Groups[1].Value }
    }
    return 0
}
$candidatos = @()
if ($env:JAVA_HOME) { $candidatos += $env:JAVA_HOME }
$candidatos += Get-ChildItem "$HOME\.jdks" -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName
$candidatos += Get-ChildItem "$env:ProgramFiles\Java", "$env:ProgramFiles\Eclipse Adoptium", "$env:ProgramFiles\Microsoft" -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName
$candidatos += "$env:ProgramFiles\Android\Android Studio\jbr"
$jdk = $candidatos | Where-Object { $_ -and (Test-Path "$_\bin\java.exe") -and ((VersionJdk $_) -in 17, 21) } | Select-Object -First 1
if ($jdk) { $env:JAVA_HOME = $jdk; Ok "JDK $(VersionJdk $jdk) ($jdk)" }
else { Falta "Un JDK 17 o 21" "Instálalo con: winget install EclipseAdoptium.Temurin.21.JDK   (el JDK 25 no sirve)" }

# 3. SDK de Android (plataforma 35 y herramientas de compilación)
$sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk") |
    Where-Object { $_ -and (Test-Path "$_\platforms\android-35") -and (Test-Path "$_\build-tools") } | Select-Object -First 1
if ($sdk) {
    Ok "SDK de Android ($sdk)"
    $lp = Join-Path $raiz 'android\local.properties'
    if (-not (Test-Path $lp)) { "sdk.dir=" + $sdk.Replace([char]92, "/") | Set-Content $lp -Encoding ASCII }
}
else { Falta "El SDK de Android con la plataforma 35" "Instala Android Studio (https://developer.android.com/studio), ábrelo una vez y en More Actions > SDK Manager marca 'Android 15 (API 35)'." }

Write-Host ""
if ($faltan.Count -gt 0) {
    Write-Host "No se puede armar todavía. Falta:" -ForegroundColor Red
    $faltan | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "Tip: pídele a tu Claude 'instala lo que falta para PC Hub' y vuelve a ejecutar este archivo." -ForegroundColor Gray
    if ($MyInvocation.InvocationName -eq '.') { throw 'Faltan requisitos para armar PC Hub.' } else { exit 1 }
}
Write-Host "Todo listo para armar el paquete." -ForegroundColor Green
