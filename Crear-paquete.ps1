# Arma la carpeta "PcHub-Paquete" lista para llevar (pendrive, Drive, correo comprimido):
#   - PcHub-PC\   el programa del PC (trae todo lo necesario, no hace falta instalar .NET)
#   - PcHub.apk   la app del teléfono
#   - LEEME.txt   los pasos para tu papá
# Además deja el .zip con la versión en el nombre (PcHub-Paquete-1.0.0.zip) listo para pasar.
# Uso: clic derecho en este archivo → "Ejecutar con PowerShell"  (con -SinAbrir no abre la carpeta al terminar)
param([switch]$SinAbrir)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$salida = Join-Path $raiz 'PcHub-Paquete'
$version = (Get-Content (Join-Path $raiz 'VERSION') -Raw).Trim()

# Comprueba .NET, JDK 17/21 y el SDK de Android (y explica en español qué falta)
. (Join-Path $raiz 'Revisar-requisitos.ps1')

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
# Copia dentro del programa del PC: así queda instalada y el ícono puede ofrecer la app al teléfono (QR)
Copy-Item (Join-Path $salida 'PcHub.apk') (Join-Path $salida 'PcHub-PC\PcHub.apk')

Write-Host "3/3 Guardando el código fuente y las instrucciones…" -ForegroundColor Cyan
# El código va dentro del paquete como un "bundle" de git (todo el historial en un archivo) para que su Claude
# lo use con Mejorar-PcHub.ps1 y las actualizaciones futuras se mezclen con sus cambios sin perderlos.
if (Test-Path (Join-Path $raiz '.git')) {
    if (git -C $raiz status --porcelain -- agent android PROTOCOL.md VERSION CLAUDE.md Crear-paquete.ps1 Mejorar-PcHub.ps1) {
        Write-Host "  Aviso: hay cambios sin guardar en git; el código del paquete no los incluye (haz commit y vuelve a armar)." -ForegroundColor Yellow
    }
    # Solo desde la rama principal (quien arma las entregas); en la copia de su Claude (rama "papa") no hace falta
    git -C $raiz rev-parse --verify --quiet main *> $null
    if ($LASTEXITCODE -eq 0 -and (git -C $raiz branch --show-current).Trim() -eq 'main') {
        git -C $raiz bundle create (Join-Path $salida 'PcHub-Codigo.bundle') main 2>&1 | Out-Null
    }
}
Copy-Item (Join-Path $raiz 'Mejorar-PcHub.ps1') $salida
Copy-Item (Join-Path $raiz 'Mejorar PC Hub con Claude.bat') $salida
# También dentro del programa del PC: así queda instalado y el ícono puede abrirlo
Copy-Item (Join-Path $raiz 'Mejorar-PcHub.ps1') (Join-Path $salida 'PcHub-PC')
Copy-Item (Join-Path $raiz 'Mejorar PC Hub con Claude.bat') (Join-Path $salida 'PcHub-PC')
if (Test-Path (Join-Path $salida 'PcHub-Codigo.bundle')) { Copy-Item (Join-Path $salida 'PcHub-Codigo.bundle') (Join-Path $salida 'PcHub-PC') }
Copy-Item (Join-Path $raiz 'LEEME-PAPA.txt') (Join-Path $salida 'LEEME.txt')
@"
PC Hub ${version}: cómo actualizar (2 minutos)
=============================================
1. PC: abre PcHub-PC\PcHub.Agent.exe y elige "Sí" cuando diga que hay una versión nueva.
   Cierra la anterior, la reemplaza y la abre sola.
2. Teléfono (mismo Wi-Fi): abre la app y toca "Actualizar la app ahora" en Inicio, luego "Instalar".
   (Si no sale el aviso: abre PcHub.apk a mano; se instala encima, sin desinstalar.)
Tu PIN y tus teléfonos vinculados se conservan.
Si algo falla, mira LEEME.txt.
"@ | Set-Content (Join-Path $salida 'ACTUALIZAR.txt') -Encoding UTF8

$zip = Join-Path $raiz "PcHub-Paquete-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $salida '*') -DestinationPath $zip

$mb = [math]::Round(((Get-ChildItem $salida -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
Write-Host "`nListo: $salida ($mb MB, versión $version)" -ForegroundColor Green
Write-Host "Para pasar: $zip" -ForegroundColor Green
if (-not $SinAbrir) { Start-Process explorer.exe $raiz }
