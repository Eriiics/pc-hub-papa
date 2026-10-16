@echo off
chcp 65001 >nul
title Instalar PC Hub
rem Instalador de PC Hub de UN SOLO ARCHIVO: doble clic y listo. Instala lo necesario, conecta GitHub y Claude,
rem arma el programa y la app del teléfono, instala PC Hub en este PC y abre la vinculación del teléfono por Wi-Fi.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$t=[IO.File]::ReadAllText('%~f0',[Text.Encoding]::UTF8); Invoke-Expression $t.Substring($t.LastIndexOf('#'+'PS-INICIO#')+11)"
exit /b
#PS-INICIO#
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$Host.UI.RawUI.WindowTitle = 'Instalar PC Hub'

$RepoUrl = 'https://github.com/Eriiics/pc-hub-papa.git'
$docs = [Environment]::GetFolderPath('MyDocuments')
$carpeta = if ($env:PCHUB_CARPETA_CODIGO) { $env:PCHUB_CARPETA_CODIGO } else { Join-Path $docs 'PcHub-Codigo' }
$soloPreparar = [bool]$env:PCHUB_SOLO_PREPARAR   # (para pruebas: no instala PC Hub en este PC)
$total = 6
$log = Join-Path $env:TEMP 'instalar-pchub.log'
try { Start-Transcript -Path $log -Append | Out-Null } catch { }

function Paso($n, $t) { Write-Host ""; Write-Host "[$n/$total] $t" -ForegroundColor Cyan }
function Ok($t) { Write-Host "  [OK] $t" -ForegroundColor Green }
function Info($t) { Write-Host "  $t" -ForegroundColor Gray }
function Aviso($t) { Write-Host "  [!] $t" -ForegroundColor Yellow }
function Parar($t) {
    Write-Host ""; Write-Host "  [X] $t" -ForegroundColor Red
    Write-Host "  (El detalle quedó en $log)" -ForegroundColor DarkGray
    Read-Host '  Pulsa Enter para cerrar'; exit 1
}
function RefrescarRuta {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') +
        ';' + (Join-Path $env:USERPROFILE '.local\bin')
}
function Tiene($c) { [bool](Get-Command $c -ErrorAction SilentlyContinue) }
function Instalar($id, $nombre) {
    if (-not (Tiene winget)) { Parar "No encuentro «winget» (el instalador de programas de Windows). Instala «App Installer» desde la tienda de Microsoft y vuelve a abrir este archivo." }
    Info "Instalando $nombre… (si Windows pide permiso, acéptalo)"
    winget install --id $id -e --silent --accept-package-agreements --accept-source-agreements
    RefrescarRuta
}

Clear-Host
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  PC Hub: instalación completa" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host @"

Esto deja todo listo en este PC: los programas necesarios, tu cuenta de GitHub y de Claude, PC Hub instalado
y la app del teléfono lista para instalarse por Wi-Fi. Tarda unos 15 a 30 minutos la primera vez.
Te iré avisando cuando necesite algo de ti (iniciar sesión, aceptar un permiso). No cierres esta ventana.
"@
RefrescarRuta

# ---------------------------------------------------------------- 1. programas
Paso 1 'Programas necesarios (Git, GitHub, .NET y Java)'
if (-not (Tiene git)) { Instalar 'Git.Git' 'Git' } else { Ok 'Git' }
if (-not (Tiene git)) { Parar 'No pude instalar Git.' }
if (-not (Tiene gh)) { Instalar 'GitHub.cli' 'GitHub CLI' } else { Ok 'GitHub CLI' }
$dotnetOk = $false
if (Tiene dotnet) { foreach ($l in (& dotnet --list-sdks 2>$null)) { if ($l -match '^(\d+)\.' -and [int]$Matches[1] -ge 8) { $dotnetOk = $true } } }
if (-not $dotnetOk) { Instalar 'Microsoft.DotNet.SDK.8' '.NET 8 SDK' } else { Ok '.NET SDK' }
$javaOk = $false
$busca = @("$env:ProgramFiles\Microsoft", "$env:ProgramFiles\Eclipse Adoptium", "$env:ProgramFiles\Java", "$env:ProgramFiles\Android\Android Studio", "$env:USERPROFILE\.jdks")
foreach ($jh in @($env:JAVA_HOME) + (Get-ChildItem $busca -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName) + @("$env:ProgramFiles\Android\Android Studio\jbr")) {
    if ($jh -and (Test-Path (Join-Path $jh 'bin\java.exe')) -and ($jh -match '(?<![\d.])(17|21)(?!\d)|jbr')) { $javaOk = $true; $env:JAVA_HOME = $jh; break }
}
if (-not $javaOk) {
    Instalar 'Microsoft.OpenJDK.21' 'Java 21 (JDK)'
    $jh = Get-ChildItem "$env:ProgramFiles\Microsoft" -Directory -Filter 'jdk-21*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($jh) { $env:JAVA_HOME = $jh.FullName; $javaOk = $true }
}
if ($javaOk) { Ok "Java ($env:JAVA_HOME)" } else { Aviso 'No encontré Java 21; el armado lo buscará por su cuenta.' }

# ---------------------------------------------------------------- 2. Android SDK
Paso 2 'Herramientas de Android (para armar la app del teléfono)'
$sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
if (-not (Test-Path (Join-Path $sdk 'platforms\android-35')) -or -not (Test-Path (Join-Path $sdk 'build-tools'))) {
    $sm = Join-Path $sdk 'cmdline-tools\latest\bin\sdkmanager.bat'
    if (-not (Test-Path $sm)) {
        Info 'Descargando las herramientas de línea de comandos de Android…'
        $zip = Join-Path $env:TEMP 'cmdline-tools.zip'
        try { Invoke-WebRequest 'https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip' -OutFile $zip -UseBasicParsing }
        catch { Parar 'No pude descargar las herramientas de Android (¿hay internet?).' }
        $tmp = Join-Path $env:TEMP 'cmdline-tools-x'
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
        Expand-Archive $zip $tmp -Force
        New-Item -ItemType Directory (Join-Path $sdk 'cmdline-tools') -Force | Out-Null
        Move-Item (Join-Path $tmp 'cmdline-tools') (Join-Path $sdk 'cmdline-tools\latest') -Force
    }
    Info 'Aceptando licencias e instalando la plataforma 35 (descarga unos 200 MB)…'
    $env:ANDROID_HOME = $sdk
    (1..40 | ForEach-Object { 'y' }) | & $sm --licenses 2>&1 | Out-Null
    & $sm 'platform-tools' 'platforms;android-35' 'build-tools;35.0.0' 2>&1 | ForEach-Object { if ($_ -match 'Installing|Unzipping|done') { Info $_ } }
}
if (Test-Path (Join-Path $sdk 'platforms\android-35')) { Ok "SDK de Android ($sdk)" } else { Parar 'No se pudo instalar el SDK de Android.' }
[Environment]::SetEnvironmentVariable('ANDROID_HOME', $sdk, 'User'); $env:ANDROID_HOME = $sdk

# ---------------------------------------------------------------- 3. Claude Code
Paso 3 'Claude Code (tu asistente)'
if (-not (Tiene claude)) {
    Info 'Instalando Claude Code…'
    try { Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression } catch { Aviso "No pude instalarlo solo: $($_.Exception.Message)" }
    RefrescarRuta
}
if (Tiene claude) {
    Ok 'Claude Code instalado'
    claude auth status *> $null
    if ($LASTEXITCODE -ne 0) {
        Info 'Ahora inicia sesión con tu cuenta de Claude (se abre el navegador)…'
        claude auth login --claudeai
    }
    claude auth status *> $null
    if ($LASTEXITCODE -eq 0) { Ok 'Sesión de Claude lista' } else { Aviso 'Falta iniciar sesión en Claude; podrás hacerlo después desde el ícono de PC Hub (Configurar asistente IA).' }
} else { Aviso 'Claude Code no quedó instalado; podrás instalarlo después desde el ícono de PC Hub (Configurar asistente IA).' }

# ---------------------------------------------------------------- 4. GitHub y el código
Paso 4 'GitHub y el código de PC Hub'
gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Info 'Para que tus cambios se suban a GitHub hace falta una cuenta (gratis; si no tienes, créala en https://github.com/signup).'
    $r = Read-Host '  ¿Conectar tu cuenta de GitHub ahora? Pulsa Enter para sí, o escribe N para hacerlo después'
    if ($r -notmatch '^[nN]') {
        Info 'Aparecerá un código; pulsa Enter, se abre el navegador, escribe el código e inicia sesión con TU cuenta.'
        gh auth login --hostname github.com --git-protocol https --web
    }
}
gh auth status *> $null
if ($LASTEXITCODE -eq 0) {
    gh auth setup-git *> $null
    Ok 'GitHub conectado'
    if (-not (git config --global user.name)) {
        $login = (gh api user --jq .login 2>$null)
        $nombre = (gh api user --jq .name 2>$null); if (-not $nombre -or $nombre -eq 'null') { $nombre = $login }
        $mail = (gh api user --jq .email 2>$null); if (-not $mail -or $mail -eq 'null') { $mail = "$login@users.noreply.github.com" }
        git config --global user.name $nombre; git config --global user.email $mail
    }
} else {
    Aviso 'Sin cuenta de GitHub por ahora: PC Hub funciona igual, pero los cambios no se subirán hasta que la conectes (vuelve a abrir este archivo).'
    if (-not (git config --global user.name)) { git config --global user.name 'PC Hub'; git config --global user.email 'pchub@example.com' }
}
git config --global pull.rebase false
if (Test-Path (Join-Path $carpeta '.git')) {
    Info "Ya existe $carpeta; trayendo los cambios nuevos…"
    git -C $carpeta pull --no-rebase --no-edit origin main 2>&1 | Out-Null
} else {
    for (;;) {
        Info "Descargando el código a $carpeta …"
        git clone $RepoUrl $carpeta 2>&1 | Out-Null
        if (Test-Path (Join-Path $carpeta '.git')) { break }
        Aviso 'No pude descargar el código (¿hay internet? ¿el repositorio es privado y falta tu cuenta o la invitación?).'
        $r = Read-Host '  Pulsa Enter para reintentar (o escribe S para salir)'
        if ($r -match '^[sS]') { Parar 'Instalación detenida.' }
    }
}
git -C $carpeta checkout main 2>&1 | Out-Null
Ok "Código en $carpeta"

# ---------------------------------------------------------------- 5. armar
Paso 5 'Armando PC Hub y la app del teléfono (tarda unos minutos)'
Set-Location $carpeta
try { & (Join-Path $carpeta 'Crear-paquete.ps1') -SinAbrir } catch { Parar "No se pudo armar PC Hub: $($_.Exception.Message)" }
$exe = Join-Path $carpeta 'PcHub-Paquete\PcHub-PC\PcHub.Agent.exe'
if (-not (Test-Path $exe)) { Parar 'El armado terminó pero no encuentro PcHub.Agent.exe.' }
Ok 'PC Hub y la app del teléfono (PcHub.apk) armados'

# ---------------------------------------------------------------- 6. instalar
Paso 6 'Instalando PC Hub en este PC'
if ($soloPreparar) {
    Aviso 'Modo de prueba: no se instala PC Hub en este PC.'
} else {
    Info 'PC Hub se instala (queda en el menú Inicio: busca «PC Hub») y se abre. Si Windows pregunta por el firewall, elige «Permitir acceso».'
    Start-Process $exe -ArgumentList '--actualizar', '--vincular'
    Start-Sleep -Seconds 6
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Listo. Solo falta vincular el teléfono." -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host @"

En la ventana «Instalar por Wi-Fi» que se abrió sigue los pasos (activar Depuración inalámbrica en el teléfono,
escribir el código que muestra) y toca «Instalar en el teléfono ahora»: la app queda instalada sola.
Si cierras PC Hub, ábrelo escribiendo «PC Hub» en el buscador de Windows (o con el ícono del escritorio).

Para pedir cambios: en el teléfono, Inicio → «¿Quieres cambiar algo de PC Hub?».
"@
try { Stop-Transcript | Out-Null } catch { }
Read-Host 'Pulsa Enter para cerrar'
