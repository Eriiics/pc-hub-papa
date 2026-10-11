# PC Hub

Controla tu PC con Windows desde el teléfono Android: touchpad, puntero con giroscopio, teclado, música,
YouTube, ventanas, aplicaciones, **Excel** y un asistente con IA (Claude) que acepta voz.

```
pc-hub-papa/
├── agent/PcHub.Agent/   ← programa para Windows (C# / .NET 8), vive en la bandeja del sistema
├── android/             ← app Android (Kotlin + Jetpack Compose)
├── Crear-paquete.ps1    ← arma la carpeta lista para llevar
├── LEEME-PAPA.txt       ← instrucciones paso a paso para quien lo usa
└── PROTOCOL.md          ← mensajes entre el teléfono y el PC
```

## Para quien lo usa

Abre `LEEME.txt` (viene dentro del paquete): son tres pasos, instalar en el PC, instalar en el teléfono y poner el PIN.

## Para armar el paquete

1. Necesitas en este PC: .NET 8 SDK, el SDK de Android y un JDK 17 o 21.
2. Clic derecho en `Crear-paquete.ps1` → *Ejecutar con PowerShell*.
3. Se crea `PcHub-Paquete/` con `PcHub-PC/` (el programa, con todo incluido: no hay que instalar .NET), `PcHub.apk` y `LEEME.txt`.
   Comprímela en un .zip y pásala por pendrive, Drive o correo.

Para actualizar: vuelve a armar el paquete y pásalo. Al abrir `PcHub.Agent.exe` ofrece instalar y reemplaza la versión anterior
(la configuración y los dispositivos vinculados se conservan).

## Excel

Pantalla **Excel** (en Módulos): controla el Excel que está abierto en el PC, sin instalar nada extra.

- Ver la hoja alrededor de la celda activa y tocar una celda para seleccionarla; flechas para moverte.
- Escribir texto, números o fórmulas (`=SUMA(A1:A5)`) y pasar a la celda de abajo o de la derecha.
- Sumar los números de arriba, negrita, borrar celda, filtro, cambiar/crear hojas, deshacer y guardar.
- Archivos recientes (Recientes de Windows, Documentos, Escritorio, Descargas) para abrirlos desde el teléfono, y libro nuevo.
- Si Excel está ocupado (editando una celda o con un cuadro abierto) avisa y hay que terminar eso en el PC.

## Asistente con IA

Clic derecho en el ícono de PC Hub → *Configurar asistente IA…* → *Usar mi suscripción de Claude (Claude Code)*. Usa el plan de
claude.ai sin API key ni costo extra. También acepta una API key si se prefiere.

## Resto de módulos

Touchpad, puntero (giroscopio), reproductor y YouTube / YouTube Music, ventanas, aplicaciones, sistema (energía y portapapeles),
terminal PowerShell, comandos personalizados y reglas de contexto (la app cambia de pantalla según la ventana activa del PC).
Los comandos y reglas se editan en `%APPDATA%\PcHub\config.json` (clic derecho en el ícono → *Abrir configuración*).
