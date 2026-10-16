# Instalar y configurar PC Hub con Claude Code (instrucciones para tu Claude)

> **Para la persona:** abre una terminal (clic derecho en el botón Inicio → "Terminal" o "Windows PowerShell"), escribe `claude` y pulsa Enter. Cuando Claude esté listo, pégale esta frase:
>
> *"Lee las instrucciones de https://raw.githubusercontent.com/Eriiics/pc-hub-papa/main/CONFIGURAR-CON-CLAUDE.md y haz todo lo que dicen."*
>
> (Si ya tienes este archivo en el PC, también vale: *"Lee el archivo CONFIGURAR-CON-CLAUDE.md y haz todo lo que dice."*)
>
> Después solo contesta lo que te pregunte y acepta los permisos de Windows cuando salgan.

```
REPO_URL: https://github.com/Eriiics/pc-hub-papa.git
```
*(Si esa línea cambia o dice `PON-AQUI`, Claude le pedirá la dirección a la persona.)*

---

## Instrucciones para Claude

Eres el Claude Code de una persona que **usa mucho Excel y Claude Code, pero no programa**. Habla siempre en español sencillo, sin jerga, y cuéntale en una o dos frases qué vas a hacer antes de cada paso grande. Haz todo tú: no le pidas que ejecute comandos. Solo pídele lo que únicamente ella puede hacer (aceptar un permiso de Windows, iniciar sesión en el navegador, activar algo en el teléfono, leerte un código).

**Objetivo:** que al terminar esta persona tenga:
1. **PC Hub instalado en este PC**: aparece en el menú Inicio (se encuentra escribiendo "PC Hub" en el buscador de Windows), tiene ícono en el escritorio, está en Configuración → Aplicaciones y se abre solo al encender el PC.
2. **La app instalada en su teléfono** por Wi-Fi (depuración inalámbrica), ya conectada con el PC.
3. **El código en `Documentos\PcHub-Codigo`**, conectado al repositorio de GitHub, para poder pedirle mejoras a su Claude desde la app (cada mejora se guarda y se sube al repositorio, y los cambios nuevos del repositorio salen como "Actualizar").

Trabaja en este orden. Si un paso ya está hecho, salta al siguiente. Si un paso falla, lee el error, corrígelo y vuelve a intentar; no se lo devuelvas a la persona.

### 1. Averigua dónde estás y qué hay
- Revisa qué programas existen: `git --version`, `gh --version`, `dotnet --list-sdks`, `java -version`, `claude --version`, y si hay SDK de Android (`$env:LOCALAPPDATA\Android\Sdk`). Comprueba si PC Hub ya está instalado (`$env:LOCALAPPDATA\Programs\PcHub\PcHub.Agent.exe`).
- Carpeta del código: `Documentos\PcHub-Codigo` (usa `[Environment]::GetFolderPath('MyDocuments')`; con OneDrive puede ser otra ruta). **Tiene que ser esa carpeta**: PC Hub la busca ahí.

### 2. Instala lo que falte (usa `winget`; Windows puede pedir permiso de administrador: avísale antes)
- **Git:** `winget install --id Git.Git -e --accept-package-agreements --accept-source-agreements`
- **GitHub CLI:** `winget install --id GitHub.cli -e --accept-package-agreements --accept-source-agreements`
- **.NET SDK 8 o más nuevo:** `winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements`
- **JDK 21** (Gradle necesita 17 o 21; con la 25 falla): `winget install --id Microsoft.OpenJDK.21 -e --accept-package-agreements --accept-source-agreements`
- **SDK de Android** (plataforma 35). Sin Android Studio, solo las herramientas de línea de comandos:
  1. Descarga el zip "Command line tools only" de https://developer.android.com/studio#command-line-tools-only (por ejemplo `https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip`; confirma el enlace en esa página si falla).
  2. Descomprímelo de modo que quede `%LOCALAPPDATA%\Android\Sdk\cmdline-tools\latest\bin\sdkmanager.bat`.
  3. Con `JAVA_HOME` apuntando al JDK: acepta las licencias (`sdkmanager --licenses`, respondiendo `y`) y ejecuta
     `sdkmanager "platform-tools" "platforms;android-35" "build-tools;35.0.0"`.
  4. Deja `ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk` para el usuario (`setx ANDROID_HOME ...`).
- Después de instalar cosas, **actualiza el PATH de la sesión** (o abre una terminal nueva) para que se vean los programas.
- **Claude Code** ya está instalado (eres tú). Comprueba con `claude auth status` que la sesión sea de claude.ai.

No cambies versiones de Gradle, AGP, Kotlin ni de librerías del proyecto bajo ninguna circunstancia.

### 3. Descarga el código de PC Hub
1. Dirección del repositorio: la de `REPO_URL` arriba (si dice `PON-AQUI`, pídesela a la persona).
2. Si `Documentos\PcHub-Codigo` **no existe**: `git clone <REPO_URL> "<Documentos>\PcHub-Codigo"`. Si el repositorio es privado y falla por permisos, ve al paso 4 primero y vuelve a intentar.
3. Si ya existe y es un repositorio: comprueba `git remote get-url origin` (corrígelo con `git remote set-url origin <REPO_URL>` si hace falta), `git checkout main` y `git pull --no-rebase origin main`.
4. Comprueba que existen `android/pchub.keystore` y `VERSION` (sin ese `keystore` el teléfono rechazaría las actualizaciones; **nunca lo borres**).

### 4. Cuenta de GitHub (para subir los cambios)
- Para **descargar** el código no hace falta cuenta si el repositorio es público. Para **subir** las mejoras que se pidan desde la app, sí.
- Pregúntale a la persona si ya tiene cuenta de GitHub. Si no, ayúdale a crearla gratis en https://github.com/signup (ella escribe sus datos en el navegador) y espera a que te avise.
- `gh auth login --hostname github.com --git-protocol https --web` (explícale que aparece un código, que lo escriba en el navegador que se abre e inicie sesión con su cuenta) y después `gh auth setup-git`.
- Configura tu identidad de git si falta: nombre y correo de la persona (o `gh api user`), `git config --global user.name`/`user.email`, y `git config --global pull.rebase false`.
- Si la persona prefiere no crearla ahora, sigue: todo funciona igual, solo que los cambios no se subirán hasta que la conecte (dile que repita este paso cuando quiera).

### 5. Arma PC Hub y la app del teléfono
- Dentro de `Documentos\PcHub-Codigo`: `./Revisar-requisitos.ps1` debe terminar en "Todo listo" (si no, vuelve al paso 2).
- Ejecuta `./Crear-paquete.ps1 -SinAbrir` y deja que corra (varios minutos). Debe terminar en "Listo" y crear `PcHub-Paquete\` con `PcHub-PC\PcHub.Agent.exe` y `PcHub.apk`.

### 6. Instala PC Hub en este PC
- Ejecuta `Start-Process "<...>\PcHub-Paquete\PcHub-PC\PcHub.Agent.exe" -ArgumentList '--actualizar','--vincular'`. Se instala solo (sin preguntar), se abre y muestra la ventana "Instalar por Wi-Fi".
- Avísale que Windows puede preguntar por el firewall: debe elegir **"Permitir acceso"** (redes privadas).
- Comprueba: el proceso `PcHub.Agent` está corriendo desde `%LOCALAPPDATA%\Programs\PcHub`, existe el acceso directo del menú Inicio ("PC Hub") y el del escritorio. Dile cómo abrirlo si lo cierra: escribir **PC Hub** en el buscador de Windows o usar el ícono del escritorio.

### 7. Instala la app en el teléfono por Wi-Fi
Guía a la persona paso a paso (el teléfono y el PC deben estar en el **mismo Wi-Fi**):
1. En el teléfono: Ajustes → Acerca del teléfono → Información de software → tocar **7 veces** "Número de compilación" (activa Opciones de desarrollador).
2. Ajustes → Opciones de desarrollador → activar **"Depuración inalámbrica"** → tocar **"Vincular dispositivo con código de vinculación"**: muestra una dirección con puerto (como `192.168.1.20:37215`) y un código de 6 números. (En un Samsung, si no deja: Ajustes → Seguridad y privacidad → Bloqueador automático → desactivarlo.)
3. En la ventana "Instalar por Wi-Fi" de PC Hub (si se cerró: clic derecho en el ícono de PC Hub junto al reloj → "Instalar en el teléfono por Wi-Fi (sin tocar nada)…"), escribir esa dirección y ese código y tocar **Vincular**, y luego **"Instalar en el teléfono ahora"**.
   - *Alternativa si la ventana da problemas:* hazlo tú con el `adb` incluido: `<...>\PcHub-Paquete\PcHub-PC\herramientas\adb\adb.exe pair <dirección:puerto> <código>` (la persona te dicta ambos), luego `adb.exe mdns services` / `adb.exe connect <ip:puerto de la pantalla principal de Depuración inalámbrica>` y `adb.exe install -r "<...>\PcHub-Paquete\PcHub.apk"`.
4. La app se abre sola en el teléfono. Si pide el PIN (6 números), está en el ícono de PC Hub del PC (doble clic). Desde ahora la app se reconecta sola y se actualiza sola por Wi-Fi.

### 8. Explícale cómo funciona (en sencillo, sin comandos)
- Para pedir un cambio: en el teléfono, **Inicio → "¿Quieres cambiar algo de PC Hub?"** (o en el PC, clic derecho en el ícono de PC Hub → "Pedir una mejora a mi Claude…"). Anota lo que quiera y elige "Hacer esta" o "Hacer todas".
- Cada cambio se guarda y se sube solo al repositorio de GitHub (si conectó su cuenta).
- Si alguien sube un cambio al repositorio, en Inicio sale **"Actualizar ahora"**: se baja, se arma y se instala solo.
- Si algo no le gusta, en la lista de mejoras hay "Deshacer esta mejora".

### 9. Termina
Resumen corto para la persona: qué quedó instalado (PC Hub en el PC y la app en el teléfono), dónde está el código (`Documentos\PcHub-Codigo`), si la cuenta de GitHub quedó conectada o falta, y cómo abrir PC Hub si lo cierra. Si algo quedó pendiente, dilo claro y di qué hacer.

---

## Reglas para cualquier trabajo posterior en este proyecto
- Trabaja siempre en `Documentos\PcHub-Codigo`, rama `main`. Lee `CLAUDE.md` de esa carpeta antes de cambiar nada.
- **Cada cambio terminado se guarda y se sube:** `git add -A`, `git commit -m "<qué hiciste>"`, `git pull --no-rebase origin main` y `git push origin main`. Si hay conflictos, conserva lo de ambos lados (en `VERSION` deja el número más alto).
- Cada entrega sube el número de `VERSION` (por ejemplo 1.4.0 → 1.4.1); sin eso Android no instala la app encima.
- Nunca reescribas el historial (`push --force`, `rebase`) ni borres `android/pchub.keystore`.
