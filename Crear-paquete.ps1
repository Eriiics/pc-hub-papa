# Arma la carpeta "PcHub-Paquete" lista para llevar (pendrive, Drive, correo comprimido):
#   - PcHub-PC\   el programa del PC (trae todo lo necesario, no hace falta instalar .NET)
#   - PcHub.apk   la app del teléfono
#   - LEEME.txt   los pasos para tu papá
# Uso: clic derecho en este archivo → "Ejecutar con PowerShell"
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$salida = Join-Path $raiz 'PcHub-Paquete'
$version = (Get-Content (Join-Path $raiz 'VERSION') -Raw).Trim()

# JDK 17 o 21 para Gradle (el JDK 25 todavía no sirve con estas versiones de Gradle/Kotlin)
if (-not $env:JAVA_HOME -or ((& "$env:JAVA_HOME\bin\java.exe" -version 2>&1 | Out-String) -match 'version "2[2-9]')) {
    $jdk = Get-ChildItem "$HOME\.jdks" -Directory -ErrorAction SilentlyContinue | Where-Object Name -match '21|17' | Select-Object -First 1
    if ($jdk) { $env:JAVA_HOME = $jdk.FullName }
}
if (-not $env:JAVA_HOME) { throw 'No encontré un JDK 17 o 21. Define JAVA_HOME.' }

if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
New-Item -ItemType Directory $salida | Out-Null

Write-Host "1/3 Compilando el programa del PC…" -ForegroundColor Cyan
dotnet publish (Join-Path $raiz 'agent\PcHub.Agent\PcHub.Agent.csproj') -c Release -r win-x64 --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $salida 'PcHub-PC')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del programa del PC.' }

Write-Host "2/3 Compilando la app del teléfono (versión $version)…" -ForegroundColor Cyan
Push-Location (Join-Path $raiz 'android')
try {
    & .\gradlew.bat assembleRelease --console=plain
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la app.' }
} finally { Pop-Location }
Copy-Item (Join-Path $raiz 'android\app\build\outputs\apk\release\app-release.apk') (Join-Path $salida 'PcHub.apk')

Write-Host "3/3 Escribiendo las instrucciones…" -ForegroundColor Cyan
Copy-Item (Join-Path $raiz 'LEEME-PAPA.txt') (Join-Path $salida 'LEEME.txt')

$mb = [math]::Round(((Get-ChildItem $salida -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
Write-Host "`nListo: $salida ($mb MB, versión $version)" -ForegroundColor Green
Start-Process explorer.exe $salida
