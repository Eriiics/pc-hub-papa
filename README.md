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

Para actualizar: vuelve a armar el paquete (con `VERSION` más alta). Al abrir `PcHub.Agent.exe` avisa que hay una versión nueva, cierra la anterior y la reemplaza
(la configuración y los dispositivos vinculados se conservan); el instalador también deja la APK nueva junto al programa.
Después, en el teléfono, Inicio muestra «Actualizar la app ahora»: descarga la APK desde el PC por el Wi-Fi y abre el instalador de Android
(un toque en «Instalar»; Android no permite saltarse ese toque). La APK se instala encima porque siempre se firma con `android/pchub.keystore`.

## Configuración inicial (para quien le deja esto a otra persona)

Se hace una sola vez, con el teléfono y el PC a mano:

1. Arma el paquete (`Crear-paquete.ps1`) y abre `PcHub-PC\PcHub.Agent.exe` en el PC de él → «Sí» para instalar. Permite el firewall (redes privadas).
2. Clic derecho en el ícono → *Instalar la app en el teléfono…* y escanea el QR (mismo Wi-Fi). En el teléfono acepta «instalar apps desconocidas»
   (el mismo permiso sirve después para las actualizaciones) y, en un Samsung, sigue la sección del LEEME (Auto Blocker, Play Protect, batería sin restricciones).
3. Conecta con el PIN. Clic derecho → *Configurar asistente IA…* → Claude Code.
4. Instala en su PC lo que necesita Claude para recompilar: .NET 8 SDK, JDK 17 o 21 y el SDK de Android (`Revisar-requisitos.ps1` lo explica), y descomprime
   `PcHub-Codigo.zip` en una carpeta fija (por ejemplo `Documentos\PcHub-Codigo`). Ahí `CLAUDE.md` ya le dice a su Claude cómo hacer cambios y armar versiones.
5. Prueba el ciclo completo una vez: pídele a su Claude un cambio pequeño → arma el paquete → abre el `.exe` nuevo → en el teléfono «Actualizar la app ahora».

Después de eso él no necesita cables ni pasar archivos: el teléfono se actualiza solo desde el PC por el Wi-Fi de la casa.

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
