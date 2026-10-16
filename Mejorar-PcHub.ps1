# Abre Claude Code en la carpeta con el código de PC Hub para cambiar la app con tus propias palabras.
#
#  - La primera vez deja el código en Documentos\PcHub-Codigo (una copia con historial, rama "papa").
#  - Si llegó una actualización nueva (PcHub-Codigo.bundle más reciente), la MEZCLA con tus cambios sin perderlos.
#    Si algo choca, Claude lo resuelve contigo.
#  - Después abre Claude Code ahí mismo. Pídele lo que quieras; al final dile "arma el paquete nuevo".
#
# Uso: doble clic en "Mejorar PC Hub con Claude.bat" (o clic derecho en este archivo -> Ejecutar con PowerShell)
param([string]$Bundle, [switch]$SoloActualizar)
$ErrorActionPreference = 'Stop'
$aqui = $PSScriptRoot
if (-not $Bundle) { $Bundle = Join-Path $aqui 'PcHub-Codigo.bundle' }
$carpeta = if ($env:PCHUB_CARPETA_CODIGO) { $env:PCHUB_CARPETA_CODIGO } else { Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PcHub-Codigo' }

function Paso($t) { Write-Host "▶ $t" -ForegroundColor Cyan }
function Aviso($t) { Write-Host "  $t" -ForegroundColor Yellow }
function ActualizarRuta {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') +
        ';' + (Join-Path $env:USERPROFILE '.local\bin')
}
function Salir($t) { Write-Host "`n$t" -ForegroundColor Red; Read-Host 'Pulsa Enter para cerrar'; exit 1 }

ActualizarRuta

# 1. Lo que hace falta: Git y Claude Code
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Paso 'Falta Git (el programa que guarda el historial del código). Lo instalo ahora…'
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install --id Git.Git -e --accept-package-agreements --accept-source-agreements
        ActualizarRuta
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Salir 'No pude instalar Git solo. Descárgalo de https://git-scm.com/download/win, instálalo con todo por defecto y vuelve a abrir este archivo.'
    }
}
if (-not $SoloActualizar -and -not (Get-Command claude -ErrorAction SilentlyContinue)) {
    Paso 'Falta Claude Code. Lo instalo ahora…'
    try { Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression } catch { }
    ActualizarRuta
    if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
        Salir 'No pude instalar Claude Code solo. En el ícono de PC Hub (junto al reloj): Configurar asistente IA… -> Instalar Claude Code.'
    }
}
if (-not $SoloActualizar) { claude auth status *> $null }
if (-not $SoloActualizar -and $LASTEXITCODE -ne 0) {
    Paso 'Inicia sesión con tu cuenta de Claude (se abre el navegador)…'
    claude auth login --claudeai
}

# 2. La carpeta del código: se prepara una sola vez
if (-not (Test-Path (Join-Path $carpeta '.git'))) {
    if (-not (Test-Path $Bundle)) { Salir "No encuentro $Bundle (el código de PC Hub que viene en el paquete)." }
    Paso "Preparando el código en $carpeta (solo la primera vez)…"
    git clone --quiet $Bundle $carpeta
    if ($LASTEXITCODE -ne 0) { Salir 'No pude copiar el código.' }
    git -C $carpeta checkout --quiet -b papa
    git -C $carpeta remote rename origin entrega
    git -C $carpeta fetch --quiet $Bundle '+main:refs/remotes/entrega/main' 2>$null
    if (-not (git -C $carpeta config user.name)) { git -C $carpeta config user.name 'PC Hub (papá)' }
    if (-not (git -C $carpeta config user.email)) { git -C $carpeta config user.email 'papa@pchub.local' }
}
if (-not (git -C $carpeta config user.name)) { git -C $carpeta config user.name 'PC Hub (papá)' }
if (-not (git -C $carpeta config user.email)) { git -C $carpeta config user.email 'papa@pchub.local' }

# Tus cambios siempre viven en la rama "papa"
$rama = (git -C $carpeta branch --show-current).Trim()
if ($rama -ne 'papa') {
    git -C $carpeta add -A
    git -C $carpeta commit --quiet -m 'Cambios guardados antes de cambiar de rama' 2>$null
    git -C $carpeta checkout --quiet -B papa
}

# 3. ¿Llegó una actualización? Se mezcla con lo tuyo
$pedirConflictos = $null
if (Test-Path $Bundle) {
    git -C $carpeta fetch --quiet $Bundle '+main:refs/remotes/entrega/main' 2>$null
    if ($LASTEXITCODE -eq 0) {
        git -C $carpeta merge-base --is-ancestor refs/remotes/entrega/main HEAD 2>$null
        if ($LASTEXITCODE -ne 0) {
            Paso 'Hay una actualización nueva de PC Hub. La mezclo con tus cambios…'
            # Primero se guarda todo lo tuyo para que nada se pierda
            git -C $carpeta add -A
            git -C $carpeta commit --quiet -m ('Cambios guardados antes de actualizar (' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + ')') 2>$null
            git -C $carpeta merge refs/remotes/entrega/main --no-edit -m 'Actualización recibida' *> $null
            if ($LASTEXITCODE -ne 0) {
                Aviso 'La actualización y tus cambios tocaron las mismas líneas. Claude lo va a resolver contigo.'
                $pedirConflictos = 'Acabo de recibir una actualización de PC Hub y chocó con mis cambios (git merge con conflictos). ' +
                    'Resuélvelos conservando LO QUE YO PEDÍ ANTES y también lo nuevo de la actualización; si es VERSION, deja el número más alto. ' +
                    'Cuando termines, haz git add -A y el commit de la mezcla, explícame en sencillo qué decidiste y pregúntame si armo el paquete nuevo.'
            } else {
                Write-Host '  Listo: la actualización quedó mezclada con tus cambios.' -ForegroundColor Green
                Write-Host '  Dile a Claude: "arma el paquete nuevo" para instalarla en el PC y el teléfono.' -ForegroundColor Green
            }
        }
    }
}
if ($SoloActualizar) { exit 0 }

# 4. Claude Code, en la carpeta del código
Paso 'Abriendo Claude Code…  (escribe lo que quieres cambiar; para salir: /exit)'
Set-Location $carpeta
if ($pedirConflictos) { claude $pedirConflictos } else { claude }
