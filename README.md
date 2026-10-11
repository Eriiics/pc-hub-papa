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

1. Necesitas en este PC: .NET 8 SDK, el SDK de Android y un JDK 17 o 21. `Revisar-requisitos.ps1` lo comprueba y explica qué falta (`Crear-paquete.ps1` lo ejecuta solo).
2. Sube `VERSION` si es una entrega nueva (la APK solo se instala encima si el número es mayor).
3. Clic derecho en `Crear-paquete.ps1` → *Ejecutar con PowerShell*.
4. Se crea `PcHub-Paquete/` con `PcHub-PC/` (el programa, con todo incluido: no hay que instalar .NET), `PcHub.apk`, `LEEME.txt`, `ACTUALIZAR.txt` y `PcHub-Codigo.zip` (el código fuente, para cambiar PC Hub con Claude Code), más `PcHub-Paquete-<versión>.zip` listo para pasar por pendrive, Drive o correo.

Para actualizar: vuelve a armar el paquete y pásalo. Al abrir `PcHub.Agent.exe` avisa que hay una versión nueva, cierra la anterior y la reemplaza
(la configuración y los dispositivos vinculados se conservan). La APK se instala encima porque siempre se firma con `android/pchub.keystore`.
Si el teléfono y el PC quedan con versiones distintas, la app lo avisa en Inicio.

## Excel

Pantalla **Excel** (en Módulos): controla el Excel que está abierto en el PC, sin instalar nada extra.

- Ver la hoja alrededor de la celda activa y tocar una celda para seleccionarla; flechas para moverte.
- Escribir texto, números o fórmulas (`=SUMA(A1:A5)`) y pasar a la celda de abajo o de la derecha.
- Sumar los números de arriba, negrita, borrar celda, filtro, cambiar/crear hojas, copiar/cortar/pegar, filas, saltar al borde de los datos, formato % y $, deshacer y guardar.
- Archivos recientes (Recientes de Windows, Documentos, Escritorio, Descargas) para abrirlos desde el teléfono, y libro nuevo.
- Si Excel está ocupado (editando una celda o con un cuadro abierto) avisa y hay que terminar eso en el PC.

## Asistente con IA

Clic derecho en el ícono de PC Hub → *Configurar asistente IA…* → *Usar mi suscripción de Claude (Claude Code)*. Usa el plan de
claude.ai sin API key ni costo extra. También acepta una API key si se prefiere.

## Resto de módulos

Touchpad, puntero (giroscopio), reproductor y YouTube / YouTube Music, ventanas, aplicaciones, sistema (energía y portapapeles),
comandos personalizados y reglas de contexto (la app cambia de pantalla según la ventana activa del PC).
Los comandos y reglas se editan en `%APPDATA%\PcHub\config.json` (clic derecho en el ícono → *Abrir configuración*).
