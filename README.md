# PC Hub

Controla tu PC con Windows desde el teléfono Android: touchpad, puntero con giroscopio, teclado, música,
YouTube, ventanas, aplicaciones, **Excel** y un asistente con IA (Claude) que acepta voz.

```
pc-hub-papa/
├── agent/PcHub.Agent/   ← programa para Windows (C# / .NET 8), vive en la bandeja del sistema
├── android/             ← app Android (Kotlin + Jetpack Compose)
├── Crear-paquete.ps1    ← arma la carpeta lista para llevar
├── Mejorar-PcHub.ps1    ← abre Claude Code con el código (y mezcla las actualizaciones con los cambios de su Claude)
├── LEEME-PAPA.txt       ← instrucciones paso a paso para quien lo usa
└── PROTOCOL.md          ← mensajes entre el teléfono y el PC
```

## Para quien lo usa

Abre `LEEME.txt` (viene dentro del paquete): son tres pasos, instalar en el PC, instalar en el teléfono y poner el PIN.

## Para armar el paquete

1. Necesitas en este PC: .NET 8 SDK, el SDK de Android y un JDK 17 o 21. `Revisar-requisitos.ps1` lo comprueba y explica qué falta (`Crear-paquete.ps1` lo ejecuta solo).
2. Sube `VERSION` si es una entrega nueva (la APK solo se instala encima si el número es mayor).
3. Clic derecho en `Crear-paquete.ps1` → *Ejecutar con PowerShell*.
4. Se crea `PcHub-Paquete/` con `PcHub-PC/` (el programa, con todo incluido: no hay que instalar .NET), `PcHub.apk`, `LEEME.txt`, `ACTUALIZAR.txt` y `PcHub-Codigo.bundle` (el código con su historial) más `Mejorar-PcHub.ps1` (para cambiar PC Hub con Claude Code), más `PcHub-Paquete-<versión>.zip` listo para pasar por pendrive, Drive o correo.

Para actualizar: vuelve a armar el paquete (con `VERSION` más alta). Al abrir `PcHub.Agent.exe` avisa que hay una versión nueva, cierra la anterior y la reemplaza
(la configuración y los dispositivos vinculados se conservan); el instalador también deja la APK nueva junto al programa.
Después, en el teléfono, Inicio muestra «Actualizar la app ahora»: descarga la APK desde el PC por el Wi-Fi y abre el instalador de Android
(un toque en «Instalar»; Android no permite saltarse ese toque). La APK se instala encima porque siempre se firma con `android/pchub.keystore`.

## Configuración inicial (una sola vez, cuando le dejas esto a otra persona)

1. Arma el paquete (`Crear-paquete.ps1`; haz commit antes) y abre `PcHub-PC\PcHub.Agent.exe` en el PC de él → «Sí» para instalar. Permite el firewall (redes privadas).
2. Clic derecho en el ícono → *Instalar la app en el teléfono…*, escanea el QR (mismo Wi-Fi) y en la página: *Descargar la app* → instalar (acepta «instalar apps desconocidas»; el mismo permiso sirve después para las actualizaciones) → volver a la página → *Conectar con este PC*. La app queda emparejada sin escribir IP ni PIN. En un Samsung, sigue la sección del LEEME (Auto Blocker, Play Protect, batería sin restricciones).
3. Asistente: clic derecho → *Configurar asistente IA…*. Si falta Claude Code, el botón *Instalar Claude Code*; luego *Iniciar sesión con mi cuenta* (se abre el navegador en el PC; es el mejor camino porque ahí es donde corre Claude).
4. Mejorar con su Claude: en el teléfono (Inicio → «¿Quieres cambiar algo de PC Hub?», o Módulos → Mejoras) o en el PC (clic derecho → *Pedir una mejora a mi Claude…*) anota peticiones en una lista y elige «Hacer esta» o «Hacer todas». El agente (`Modules/MejorasModule.cs`) prepara `Documentos\PcHub-Codigo` (rama `papa`) con `Mejorar-PcHub.ps1`, corre Claude Code (`claude -p`, modelo sonnet, con la suscripción) una vez por mejora, guarda cada una en su commit, sube el último número de `VERSION`, arma el paquete (`Crear-paquete.ps1`, con un reintento donde Claude corrige errores de compilación) y ofrece «Instalar la versión nueva» (`PcHub.Agent.exe --actualizar`). Además hay *Abrir Claude con el código (avanzado)…* para trabajar a mano. Necesita en su PC: Git, .NET 8 SDK, JDK 17/21 y el SDK de Android (`Revisar-requisitos.ps1` lo explica) y la cuenta de Claude conectada.
5. Prueba el ciclo completo una vez: un cambio pequeño con su Claude → «arma el paquete nuevo» → abrir el `.exe` nuevo → en el teléfono «Actualizar la app ahora».

Después de eso no necesita cables ni pasar archivos: el teléfono se actualiza solo desde el PC por el Wi-Fi de la casa.

## Trabajar encima de los cambios de su Claude

El paquete trae `PcHub-Codigo.bundle`: todo el historial de git en un archivo (sale de la rama `main` de este repositorio). Su carpeta `Documentos\PcHub-Codigo` es un clon con su propia rama `papa`, donde su Claude guarda sus cambios.

- **Tú entregas** como siempre: cambios en `main`, sube `VERSION`, commit, `Crear-paquete.ps1` y le pasas el paquete (el bundle va dentro).
- **Él recibe**: instala el `.exe` (que deja el `PcHub-Codigo.bundle` nuevo) y el teléfono. Al arrancar PC Hub detecta el bundle nuevo (`AutoSyncPendiente`), `Mejorar-PcHub.ps1 -SoloActualizar` guarda primero todo lo suyo en un commit, trae `main` y lo mezcla en `papa` (`git merge`); si tocaron las mismas líneas, Claude (headless) lo resuelve conservando lo de ambos. Si el código cambió, arma solo la versión combinada y avisa «Instalar la versión nueva». También se puede forzar con *Traer las actualizaciones de Eric…* en la pantalla Mejoras. Cada mejora que se pide también mezcla primero lo tuyo antes de trabajar.
- **Tú quieres ver o traer lo suyo**: pídele que ejecute `git bundle create sus-cambios.bundle papa` en su carpeta y te pase el archivo; tú haces `git fetch sus-cambios.bundle papa:papa-cambios` y revisas/mezclas.
- Opcional: con un repositorio privado en GitHub podrías usar un remoto en vez del bundle; el bundle funciona sin cuenta de GitHub, por eso es el camino por defecto.

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
