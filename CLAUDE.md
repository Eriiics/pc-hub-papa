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
- Gradle necesita JDK 17 o 21 (con JDK 25 falla). `Crear-paquete.ps1` lo busca en `~/.jdks`.
- Cada módulo del agente es una clase en `Modules/`. Las rutas del protocolo se registran en `Hub.RegisterRoutes()` con `On("modulo.accion", …)`.
- En Android, cada pantalla es un `@Composable` en `ui/`. La navegación está en `ui/Navigation.kt` (enum `Screen`). Los ajustes persistentes van en `Prefs.kt`.
- Estilo visual: neón + vidrio. Usa `GlassCard`, `NeonButton`, `NeonTile`, `SegmentedTabs`, `Modifier.glass` y los colores de `Neon` (de `ui/Theme.kt` y `ui/Glass.kt`). Para un brillo de color usa `Modifier.neonShadow`, nunca `shadow` directo.
- Simple primero: nada de pantallas ni opciones técnicas que el usuario no necesite.

## Qué se quitó respecto a la versión de Chacho

FL Studio, OBS, el buzón de ideas, la compilación e instalación desde el PC (git/Gradle/adb) y las actualizaciones por GitHub.
Se agregó el módulo de Excel (`Modules/ExcelModule.cs`, `ui/ExcelScreen.kt`), que controla el Excel abierto por COM.
Para actualizar la app: compilar de nuevo con `Crear-paquete.ps1` y pasarle el paquete (el instalador reemplaza la versión anterior).

## Versiones

- `VERSION` (raíz) es la versión única de la app y del agente (x.y.z). Gradle y el .csproj la leen al compilar.
