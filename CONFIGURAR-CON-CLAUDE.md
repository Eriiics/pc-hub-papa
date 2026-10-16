# Configurar PC Hub con Claude (instrucciones para tu Claude Code)

> **Para la persona:** abre Claude Code en cualquier carpeta (por ejemplo, tu carpeta Documentos) y escríbele:
> *"Lee el archivo CONFIGURAR-CON-CLAUDE.md que está en la carpeta del paquete de PC Hub y haz todo lo que dice."*
> Después solo contesta lo que te pregunte y acepta los permisos de Windows cuando salgan.

```
REPO_URL: https://github.com/Eriiics/pc-hub-papa.git
```
*(Si esa línea cambia o dice `PON-AQUI`, Claude le pedirá la dirección a la persona.)*

---

## Instrucciones para Claude

Eres el Claude Code de una persona que **usa mucho Excel y Claude Code, pero no programa**. Habla siempre en español sencillo, sin jerga, y cuéntale en una o dos frases qué vas a hacer antes de cada paso grande. Haz todo tú: no le pidas que ejecute comandos. Solo pídele lo que únicamente ella puede hacer (aceptar un permiso de Windows, iniciar sesión en GitHub en el navegador, dar la dirección del repositorio).

**Objetivo:** dejar este PC listo para que PC Hub se pueda **cambiar y actualizar** así:
1. El código de PC Hub vive en un repositorio de GitHub (rama `main`).
2. Cada mejora que la persona pida desde la app (pantalla *Mejoras*) la hace Claude, se guarda en un commit y **se sube a GitHub**.
3. Si Eric (quien hizo PC Hub) sube cambios al mismo repositorio, a la persona le sale en la app y en el PC el aviso **"Actualizar"**, y con un toque se traen los cambios, se arma la versión nueva y se instala.

Trabaja en este orden. Si un paso ya está hecho, salta al siguiente. Si un paso falla, lee el error, corrígelo y vuelve a intentar; no se lo devuelvas a la persona.

### 1. Averigua dónde estás y qué hay
- Busca la carpeta del paquete de PC Hub: la que contiene `PcHub.apk`, `PcHub-PC`, `LEEME.txt` y este archivo (probablemente la carpeta donde está este archivo).
- Carpeta de trabajo para el código: `Documentos\PcHub-Codigo` (usa `[Environment]::GetFolderPath('MyDocuments')`; en OneDrive puede ser otra ruta). Es **obligatoria esa ruta**: PC Hub la busca ahí.
- Revisa qué programas existen: `git --version`, `gh --version`, `dotnet --list-sdks`, `java -version`, `claude --version`, y si hay SDK de Android (`$env:LOCALAPPDATA\Android\Sdk`).

### 2. Instala lo que falte (usa `winget`; Windows puede pedir permiso de administrador: avísale a la persona antes)
- **Git:** `winget install --id Git.Git -e --accept-package-agreements --accept-source-agreements`
- **GitHub CLI:** `winget install --id GitHub.cli -e --accept-package-agreements --accept-source-agreements`
- **.NET SDK 8 (o más nuevo):** `winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements`
- **JDK 21** (Gradle necesita 17 o 21; con la 25 falla): `winget install --id Microsoft.OpenJDK.21 -e --accept-package-agreements --accept-source-agreements`
- **SDK de Android** (plataforma 35). Si no hay Android Studio, instala solo las herramientas de línea de comandos:
  1. Descarga "Command line tools only" de https://developer.android.com/studio#command-line-tools-only (el zip de Windows; confirma el enlace actual en esa página).
  2. Descomprímelo en `%LOCALAPPDATA%\Android\Sdk\cmdline-tools\latest\` (debe quedar `...\latest\bin\sdkmanager.bat`).
  3. Acepta las licencias y baja lo necesario:
     `sdkmanager --licenses` (responde `y` a todo) y
     `sdkmanager "platform-tools" "platforms;android-35" "build-tools;35.0.0"`.
  4. Define `ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk` para el usuario (`setx ANDROID_HOME ...`).
- Después de instalar cosas, **actualiza el PATH de la sesión** (o abre una terminal nueva) para que se vean los programas.
- **Claude Code** ya está instalado (eres tú). Comprueba con `claude auth status` que la sesión sea de claude.ai.

No cambies versiones de Gradle, AGP, Kotlin ni de librerías del proyecto bajo ninguna circunstancia.

### 3. Conecta con GitHub
- `gh auth status`. Si no hay sesión: `gh auth login --hostname github.com --git-protocol https --web` — explícale a la persona que se abrirá el navegador, que ponga el código que aparece y que inicie sesión con **su** cuenta de GitHub (si no tiene, que la cree gratis en github.com y te avise).
- `gh auth setup-git` (para que `git` use esa sesión al subir cambios).
- Configura tu identidad de git si falta (`git config --global user.name` / `user.email`); pregúntale a la persona su nombre y correo, o usa los de su cuenta de GitHub (`gh api user`).
- `git config --global pull.rebase false`.

### 4. Deja el repositorio listo en `Documentos\PcHub-Codigo`
1. Dirección del repositorio: la de `REPO_URL` arriba. Si todavía dice `PON-AQUI`, pídesela a la persona (algo como `https://github.com/usuario/repositorio.git`) y úsala.
2. Comprueba que la persona tiene acceso: `git ls-remote <REPO_URL>`. Si dice que no existe o sin permiso, es que Eric aún no la invitó como colaboradora al repositorio privado: dile que le avise a Eric y que cuando le llegue la invitación (por correo o en github.com/notifications) la acepte; luego reintenta.
3. Si la carpeta `Documentos\PcHub-Codigo` **no existe**:
   - Si el repositorio **tiene contenido** (`git ls-remote --heads` muestra `main`): `git clone <REPO_URL> "<Documentos>\PcHub-Codigo"`.
   - Si el repositorio está **vacío**: descomprime `PcHub-Codigo.zip` (viene junto a este archivo en el paquete) en `Documentos\PcHub-Codigo`, y ahí ejecuta:
     `git init -b main`, `git remote add origin <REPO_URL>`, `git add -A`, `git commit -m "PC Hub: código inicial"`, `git push -u origin main`.
4. Si la carpeta ya existe y es un repositorio: comprueba `git remote get-url origin`; si apunta a otro lugar, corrígelo con `git remote set-url origin <REPO_URL>`. Asegúrate de estar en la rama `main` (`git checkout main`) y trae lo nuevo: `git pull --no-rebase origin main`.
5. Comprueba que existen `android/pchub.keystore` y `VERSION`. (Sin ese `keystore` el teléfono rechazaría las actualizaciones; **nunca lo borres**.)

### 5. Comprueba que todo compila
- Dentro de `Documentos\PcHub-Codigo`: `./Revisar-requisitos.ps1`. Debe terminar con "Todo listo". Si falta algo, vuelve al paso 2.
- Ejecuta `./Crear-paquete.ps1 -SinAbrir` (tarda unos minutos; deja que corra). Debe terminar en "Listo" y crear `PcHub-Paquete/`.
- Comprueba una subida real y sin riesgo: `git push --dry-run origin main` debe funcionar.

### 6. Explícale a la persona cómo funciona (en sencillo)
Cuéntale esto, con tus palabras y sin comandos:
- Para pedir un cambio: en el teléfono, **Inicio → "¿Quieres cambiar algo de PC Hub?"** (o en el PC, clic derecho en el ícono de PC Hub → "Pedir una mejora a mi Claude…"). Anota lo que quiera, y elige "Hacer esta" o "Hacer todas".
- Cada cambio se guarda y se sube solo a su repositorio de GitHub.
- Si Eric sube un cambio, en Inicio saldrá **"Actualizar ahora"**: se baja, se arma y se instala solo.
- Si algo no le gusta, en la lista de mejoras hay "Deshacer esta mejora".

### 7. Termina
Haz un resumen corto para la persona: qué quedó instalado, que el repositorio está conectado (di la dirección), que `Documentos\PcHub-Codigo` es la carpeta del código y que ya puede pedir mejoras desde la app. Si algo quedó pendiente (por ejemplo, la invitación al repositorio), dilo claro y di qué hacer.

---

## Reglas para cualquier trabajo posterior en este proyecto
- Trabaja siempre en `Documentos\PcHub-Codigo`, rama `main`. Lee `CLAUDE.md` de esa carpeta antes de cambiar nada.
- **Cada cambio terminado se guarda y se sube:** `git add -A`, `git commit -m "<qué hiciste>"` y `git push origin main`. Antes de subir, `git pull --no-rebase origin main`; si hay conflictos, conserva lo de ambos lados (en `VERSION` deja el número más alto).
- Cada entrega sube el número de `VERSION` (por ejemplo 1.4.0 → 1.4.1); sin eso Android no instala la app encima.
- Nunca reescribas el historial (`push --force`, `rebase`) ni borres `android/pchub.keystore`.
