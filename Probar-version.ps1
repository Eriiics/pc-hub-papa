# Sesión de prueba de PC Hub: abres, pruebas, cierras y tu PC Hub de siempre vuelve solo.
#
#  - Prueba esta versión con datos propios (%APPDATA%\PcHub-Prueba): no toca tu configuración, tus teléfonos
#    vinculados, tu PIN ni "Iniciar con Windows", y no se instala.
#  - Cierra tu PC Hub mientras dura la sesión (usan el mismo puerto) y lo vuelve a abrir al terminar.
#  - Para el teléfono hay una app aparte, "PC Hub Prueba", que convive con tu app normal sin reemplazarla.
#
# Uso (clic derecho -> "Ejecutar con PowerShell", o desde una terminal):
#   .\Probar-version.ps1                 sesión normal (compila el programa del PC y lo abre)
#   .\Probar-version.ps1 -Limpio         empieza de cero (borra los datos de la sesión anterior)
#   .\Probar-version.ps1 -Apk            además arma PcHub-Prueba.apk para instalar en el teléfono
#   .\Probar-version.ps1 -SinCompilar    usa lo que ya está compilado
# Para terminar la sesión: Salir en el ícono de la bandeja (o cierra esta ventana).
param([switch]$Limpio, [switch]$Apk, [switch]$SinCompilar)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$datos = Join-Path $env:APPDATA 'PcHub-Prueba'

function Paso($t) { Write-Host "▶ $t" -ForegroundColor Cyan }

if (-not $SinCompilar -or $Apk) { . (Join-Path $raiz 'Revisar-requisitos.ps1') }

# 1. Compilar el programa del PC (y la app de prueba si se pidió)
$proy = Join-Path $raiz 'agent\PcHub.Agent\PcHub.Agent.csproj'
if (-not $SinCompilar) {
    Paso 'Compilando el programa del PC…'
    dotnet build $proy -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del programa del PC.' }
}
$exe = Get-ChildItem (Join-Path $raiz 'agent\PcHub.Agent\bin') -Recurse -Filter PcHub.Agent.exe -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw 'No encuentro PcHub.Agent.exe compilado. Ejecuta sin -SinCompilar.' }

if ($Apk) {
    Paso 'Armando la app del teléfono "PC Hub Prueba"…'
    Push-Location (Join-Path $raiz 'android')
    try {
        & .\gradlew.bat assemblePrueba --console=plain -q
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la app de prueba.' }
    } finally { Pop-Location }
    $origen = Get-ChildItem (Join-Path $raiz 'android\app\build\outputs\apk\prueba') -Filter *.apk | Select-Object -First 1
    Copy-Item $origen.FullName (Join-Path $raiz 'PcHub-Prueba.apk') -Force
    Write-Host "   Listo: PcHub-Prueba.apk (instálala en el teléfono; queda aparte de tu app normal)" -ForegroundColor Green
}

# 2. Datos de la sesión
if ($Limpio -and (Test-Path $datos)) { Remove-Item $datos -Recurse -Force; Paso 'Datos de la sesión anterior borrados.' }

# 3. Apartar tu PC Hub (misma puerta de red) y recordar cómo reabrirlo
$tuyos = @(Get-Process PcHub.Agent -ErrorAction SilentlyContinue)
# Solo se vuelve a abrir tu copia instalada (no las compilaciones de desarrollo)
$reabrir = @($tuyos | ForEach-Object { $_.Path } | Where-Object { $_ -like '*\Programs\PcHub\*' } | Select-Object -Unique)
if ($tuyos.Count -gt 0) {
    Paso "Cerrando tu PC Hub un rato (se vuelve a abrir solo al terminar)…"
    $tuyos | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$prueba = $null
try {
    Paso 'Abriendo PC Hub en modo PRUEBA (el ícono dice "PRUEBA" al pasar el mouse)…'
    $prueba = Start-Process -FilePath $exe.FullName -ArgumentList '--prueba' -PassThru
    Write-Host "   Datos de la sesión: $datos"
    Write-Host "   PIN: doble clic en el ícono de la bandeja (junto al reloj)."
    Write-Host "   Cuando termines: clic derecho en el ícono -> Salir. Esta ventana hace el resto." -ForegroundColor Yellow
    $prueba.WaitForExit()
}
finally {
    if ($prueba -and -not $prueba.HasExited) { Stop-Process -Id $prueba.Id -Force }
    foreach ($ruta in $reabrir) {
        Paso "Volviendo a abrir tu PC Hub…"
        Start-Process -FilePath $ruta -ArgumentList '--startup'
    }
    Write-Host "Sesión terminada. Tu PC Hub quedó como estaba." -ForegroundColor Green
    Write-Host "Registro de la sesión: $datos\agent.log"
}
