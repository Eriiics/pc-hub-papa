# PC Hub (versión para el papá de Chacho): guía para Claude

App Android para controlar un PC con Windows. Es una copia simplificada de `android-pc-center`, pensada para
una persona que sabe usar el computador y el teléfono pero no programa. Usa mucho Excel y Claude Code.

- `agent/PcHub.Agent`: programa de la bandeja de Windows. C# .NET 8 WinForms, Kestrel WebSocket.
- `android/`: app del teléfono. Kotlin + Jetpack Compose, OkHttp.
- `PROTOCOL.md`: mensajes entre el teléfono y el PC. Si agregas un método nuevo, documéntalo ahí.
- `LEEME-PAPA.txt`: instrucciones para el usuario final (van dentro del paquete). Actualízalas si cambia algo que él ve.
- `Crear-paquete.ps1`: arma `PcHub-Paquete/` (programa del PC autocontenido + `PcHub.apk` + `LEEME.txt`).

## Convenciones

- Todo lo que ve el usuario va en español neutro y amigable, con tuteo. El código y los comentarios también van en español.
- No subas versiones de Gradle, AGP, Kotlin ni de las librerías: si algo choca, busca un arreglo que funcione con las versiones actuales.
- Gradle necesita JDK 17 o 21 (con JDK 25 falla). `Revisar-requisitos.ps1` (que llama `Crear-paquete.ps1`) lo busca y explica qué falta.
- Cada módulo del agente es una clase en `Modules/`. Las rutas del protocolo se registran en `Hub.RegisterRoutes()` con `On("modulo.accion", …)`.
- En Android, cada pantalla es un `@Composable` en `ui/`. La navegación está en `ui/Navigation.kt` (enum `Screen`). Los ajustes persistentes van en `Prefs.kt`.
- Estilo visual: estilo Samsung One UI (fondo negro, tarjetas grises de esquinas muy redondeadas, botones en píldora, acento azul, sin resplandores). Usa `GlassCard`, `NeonButton`, `NeonTile`, `SegmentedTabs`, `Modifier.glass` y los colores de `Neon` (de `ui/Theme.kt` y `ui/Glass.kt`); los nombres vienen de la versión neón, pero ahora dibujan el estilo plano. No agregues sombras de color ni degradados.
- Simple primero: nada de pantallas ni opciones técnicas que el usuario no necesite.

## Qué se quitó respecto a la versión de Chacho

FL Studio, OBS, el buzón de ideas, la compilación e instalación desde el PC (git/Gradle/adb) y las actualizaciones por GitHub.
Se agregó el módulo de Excel (`Modules/ExcelModule.cs`, `ui/ExcelScreen.kt`), que controla el Excel abierto por COM.
La APK se firma siempre con `android/pchub.keystore` (en git, a propósito) y su `versionCode` sale de `VERSION`: sube `VERSION` en cada entrega o Android no la instalará encima. Para actualizar la app: compilar de nuevo con `Crear-paquete.ps1`, abrir el `.exe` nuevo (el instalador reemplaza la versión anterior y deja la APK junto al programa) y en el teléfono tocar «Actualizar la app ahora» (`net/AppUpdater.kt`, descarga `/apk` del PC).

## Pruebas sin riesgo

`Probar-version.ps1` abre una sesión de prueba (`PcHub.Agent.exe --prueba`): datos en `%APPDATA%\PcHub-Prueba`, sin instalar ni tocar el inicio con Windows, y en el teléfono la app aparte `com.chacho.pchub.prueba` ("PC Hub Prueba", compilación `assemblePrueba`). Si pruebas algo, usa este modo y no el PC Hub instalado del usuario. Detalle en `PLAN-PRUEBAS.md`.

## Versiones

- `VERSION` (raíz) es la versión única de la app y del agente (x.y.z). Gradle y el .csproj la leen al compilar.

## Si eres el Claude Code de quien usa PC Hub

La persona que te habla usa mucho Excel y Claude Code pero no programa. Explícale todo en español sencillo, sin jerga, y avísale antes de cambiar algo grande.

**Cómo armar una versión nueva (pasos fijos):**
1. Haz el cambio que te pidan en `agent/` (programa del PC) y/o `android/` (app del teléfono).
2. Sube `VERSION` (por ejemplo de 1.0.0 a 1.0.1). Sin esto Android no instala la APK encima de la anterior.
3. Ejecuta `./Revisar-requisitos.ps1`. Si falta algo, explícale qué es y ayúdale a instalarlo (suele ser el SDK de .NET 8, un JDK 17/21 o el SDK de Android).
4. Ejecuta `./Crear-paquete.ps1 -SinAbrir`. Tarda unos minutos y deja `PcHub-Paquete/` y `PcHub-Paquete-<versión>.zip`.
5. Abre tú mismo `PcHub-Paquete/PcHub-PC/PcHub.Agent.exe` (`Start-Process`) y dile que acepte la actualización con «Sí». Luego, en el teléfono (mismo Wi-Fi), que abra la app y toque «Actualizar la app ahora» en Inicio y «Instalar» en Android: la app descarga la APK desde el PC (`/apk`). Se conservan el PIN y los teléfonos vinculados. Si el aviso no sale, que use «Instalar la app en el teléfono…» del ícono del PC.

**La lista de Mejoras (la persona te pide cambios desde la app):**
- Las peticiones llegan por `MejorasModule` (agente) y se te piden una a una con un mensaje que dice qué hacer. Termina siempre con una línea «RESUMEN: …» en sencillo (o «PREGUNTAS: …» si no puedes avanzar sin saber algo) y NO subas `VERSION`, ni hagas commit, ni armes el paquete: lo hace el programa.

**Cómo trabajar el código (rama y actualizaciones):**
- Esta carpeta suele ser `Documentos\PcHub-Codigo`, un clon con la rama `papa`. Trabaja siempre en `papa` y guarda cada cambio terminado con `git add -A` + `git commit -m "<qué hiciste>"`; nunca reescribas el historial.
- Eric (quien hizo PC Hub) manda actualizaciones por el archivo `PcHub-Codigo.bundle`. `Mejorar-PcHub.ps1` las mezcla en `papa` (`git merge`). Si hay conflictos, conserva lo que la persona pidió antes y también lo nuevo (en `VERSION` deja el número más alto) y cuéntale en sencillo qué decidiste.
- Prueba sin riesgo con `./Probar-version.ps1`; si tocaste `ExcelModule.cs`, corre antes de armar el paquete `PcHub.Agent.exe --autoprueba-excel` (resultado en `%TEMP%\autoprueba-excel.txt`).

**Reglas que no se rompen:**
- No cambies versiones de Gradle, AGP, Kotlin ni de librerías, aunque haya avisos.
- No borres `android/pchub.keystore`: con otra firma el teléfono rechaza la actualización.
- Si compilas y falla, lee el error, corrígelo y vuelve a intentar; no le pidas a la persona que lo resuelva.
- Todo texto que vea el usuario va en español amigable y con tuteo.
- Si agregas un método de comunicación teléfono-PC, anótalo en `PROTOCOL.md`. Si cambia algo que el usuario ve, actualiza `LEEME-PAPA.txt`.
- Antes de un cambio grande, haz una copia: `git add -A` y `git commit -m "antes de <cambio>"`.
