# PC Hub

Controla tu PC con Windows desde el teléfono Android: touchpad, puntero con giroscopio, teclado, música,
YouTube, ventanas, aplicaciones, **Excel** y un asistente con IA (Claude) que acepta voz.

```
pc-hub-papa/
├── agent/PcHub.Agent/   ← programa para Windows (C# / .NET 8), vive en la bandeja del sistema
├── android/             ← app Android (Kotlin + Jetpack Compose)
├── Crear-paquete.ps1    ← arma la carpeta lista para llevar
├── CONFIGURAR-CON-CLAUDE.md ← se lo pasas a su Claude: instala y conecta todo
├── LEEME-PAPA.txt       ← instrucciones paso a paso para quien lo usa
└── PROTOCOL.md          ← mensajes entre el teléfono y el PC
```

## Para quien lo usa

Abre `LEEME.txt` (viene dentro del paquete): son tres pasos, instalar en el PC, instalar en el teléfono y poner el PIN.

## Para armar el paquete

1. Necesitas en este PC: .NET 8 SDK, el SDK de Android y un JDK 17 o 21. `Revisar-requisitos.ps1` lo comprueba y explica qué falta (`Crear-paquete.ps1` lo ejecuta solo).
2. Sube `VERSION` si es una entrega nueva (la APK solo se instala encima si el número es mayor).
3. Clic derecho en `Crear-paquete.ps1` → *Ejecutar con PowerShell*.
4. Se crea `PcHub-Paquete/` con `PcHub-PC/` (el programa, con todo incluido: no hay que instalar .NET), `PcHub.apk`, `LEEME.txt`, `ACTUALIZAR.txt` y `PcHub-Codigo.zip` (el código, para subirlo a un repositorio vacío) más `CONFIGURAR-CON-CLAUDE.md`, más `PcHub-Paquete-<versión>.zip` listo para pasar por pendrive, Drive o correo.

Para actualizar: vuelve a armar el paquete (con `VERSION` más alta). Al abrir `PcHub.Agent.exe` avisa que hay una versión nueva, cierra la anterior y la reemplaza
(la configuración y los dispositivos vinculados se conservan); el instalador también deja la APK nueva junto al programa.
Después, en el teléfono, Inicio muestra «Actualizar la app ahora»: descarga la APK desde el PC por el Wi-Fi y abre el instalador de Android
(un toque en «Instalar»; Android no permite saltarse ese toque). La APK se instala encima porque siempre se firma con `android/pchub.keystore`.

## Configuración inicial (una sola vez, cuando le dejas esto a otra persona)

**Camino recomendado: con el Claude Code de la persona.** En su PC, abrir una terminal (Windows PowerShell), escribir `claude` y pegarle:
`Lee las instrucciones de https://raw.githubusercontent.com/Eriiics/pc-hub-papa/main/CONFIGURAR-CON-CLAUDE.md y haz todo lo que dicen.`
Es `CONFIGURAR-CON-CLAUDE.md` (el repositorio debe estar público para que pueda leerlo; también viene en el paquete): su Claude instala Git, GitHub CLI, .NET, JDK y el SDK de Android, descarga el código en `Documentos\PcHub-Codigo`, ayuda a crear/conectar la cuenta de GitHub, arma PC Hub y la APK, instala PC Hub en el PC (menú Inicio «PC Hub», ícono en el escritorio, inicio con Windows) y guía la vinculación del teléfono por Wi-Fi. Mejor la **terminal (Claude Code CLI)** que Claude Desktop: necesita ejecutar comandos, y el propio PC Hub usa el comando `claude` para las mejoras.
**Alternativa sin Claude: `Instalar-PcHub.bat`** (un solo archivo, doble clic) hace lo mismo con un script; la cuenta de GitHub es opcional. El detalle queda en `%TEMP%\instalar-pchub.log`. Los pasos de abajo son lo mismo hecho a mano.

1. **Repositorio de GitHub** (privado). Desde este proyecto, con `gh` ya iniciado en tu cuenta:
   `gh repo create <usuario>/pc-hub-papa --private --source . --remote papa --push` (sube `main`; el remoto queda con nombre `papa`), y para darle acceso: `gh api -X PUT repos/<usuario>/pc-hub-papa/collaborators/<su-usuario-de-github>`. Si prefieres que el repositorio sea de su cuenta, créalo allí y haz `git push <url> main`.
2. Arma el paquete (`Crear-paquete.ps1`; haz commit antes) y abre `PcHub-PC\PcHub.Agent.exe` en el PC de él → «Sí» para instalar. Permite el firewall (redes privadas).
3. **Instalación y actualización de la app por Wi-Fi:** en el teléfono activa Opciones de desarrollador → *Depuración inalámbrica*; en el PC, clic derecho → *Instalar en el teléfono por Wi-Fi (sin tocar nada)…*, vincula con el código de 6 dígitos y toca *Instalar en el teléfono ahora*. El PC usa su propio `adb` (viene en `PcHub-PC\herramientas\adb`) y le da a la app el permiso `WRITE_SECURE_SETTINGS`; desde entonces, cuando el PC tiene una versión más nueva, la app enciende sola la depuración inalámbrica y pide `phone.install`: el PC instala encima (`adb install -r`) y vuelve a abrir la app. Alternativa: *Instalar la app en el teléfono…* (QR → página con «Descargar la app» y «Conectar con este PC»). En un Samsung desactiva el Bloqueador automático.
4. **Su Claude lo configura todo:** pásale `CONFIGURAR-CON-CLAUDE.md` (viene en el paquete; antes cambia la línea `REPO_URL` por la dirección del repositorio). Su Claude instala Git, GitHub CLI, .NET, JDK y el SDK de Android, inicia sesión en GitHub, deja el repositorio en `Documentos\PcHub-Codigo`, comprueba que compila y le explica cómo pedir mejoras.
5. Asistente: clic derecho → *Configurar asistente IA…* (con botones para instalar Claude Code e iniciar sesión).
6. Prueba el ciclo completo una vez: una mejora desde la app → «Instalar la versión nueva» → la app del teléfono se actualiza.

## Cómo viajan los cambios (GitHub)

- El código vive en el repositorio (`origin/main`). **Todo cambio termina ahí.**
- **Mejoras desde la app** (`Modules/MejorasModule.cs`): el agente trae lo nuevo de GitHub (`git pull`), corre Claude Code (`claude -p`, modelo sonnet, con la suscripción) en `Documentos\PcHub-Codigo` una vez por mejora, guarda cada una en su commit, sube el número de `VERSION`, hace `git push origin HEAD:main`, arma el paquete (`Crear-paquete.ps1`, con un reintento donde Claude corrige errores de compilación) y ofrece «Instalar la versión nueva» (`PcHub.Agent.exe --actualizar`). Si hay conflictos al traer, Claude los resuelve conservando ambos lados.
- **Cambios que hagas tú:** edita este proyecto, sube `VERSION`, commit y `git push papa main`. El agente de su PC hace `git fetch` cada 10 minutos: cuando hay commits nuevos avisa en la bandeja y en Inicio sale «Actualizar ahora», que trae los cambios, arma la versión y la instala (PC y teléfono).
- **Para ver o traer lo que cambió su Claude:** `git pull papa main` en este proyecto.
- Sin internet o sin sesión de GitHub, las mejoras siguen funcionando en su PC y los commits se suben la próxima vez.

## Excel

Pantalla **Excel** (en Módulos): controla el Excel que está abierto en el PC, sin instalar nada extra.

- Ver la hoja alrededor de la celda activa y tocar una celda para seleccionarla; flechas para moverte.
- Escribir texto, números o fórmulas (`=SUMA(A1:A5)`) y pasar a la celda de abajo o de la derecha.
- Sumar los números de arriba, negrita, borrar celda, filtro, cambiar/crear hojas, copiar/cortar/pegar, filas, saltar al borde de los datos, formato % y $, deshacer y guardar.
- Archivos recientes (Recientes de Windows, Documentos, Escritorio, Descargas) para abrirlos desde el teléfono, y libro nuevo.
- Si Excel está ocupado (editando una celda o con un cuadro abierto) avisa y hay que terminar eso en el PC.

## Asistente con IA

Clic derecho en el ícono de PC Hub → *Configurar asistente IA…* → *Usar mi suscripción de Claude (Claude Code)* (si falta, el mismo diálogo tiene botones para instalar Claude Code e iniciar sesión). Usa el plan de
claude.ai sin API key ni costo extra. También acepta una API key si se prefiere.

## Resto de módulos

Touchpad, puntero (giroscopio), reproductor y YouTube / YouTube Music, ventanas, aplicaciones, sistema (energía y portapapeles),
comandos personalizados y reglas de contexto (la app cambia de pantalla según la ventana activa del PC).
Los comandos y reglas se editan en `%APPDATA%\PcHub\config.json` (clic derecho en el ícono → *Abrir configuración*).
