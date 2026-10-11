# PC Hub (papá de Chacho)

**Qué es:** app Android (Kotlin + Compose) y programa de bandeja de Windows (C# .NET 8, Kestrel WebSocket) para controlar el PC desde el teléfono: touchpad, teclado, música, ventanas, apps, asistente con IA y Excel. Versión simplificada de `android-pc-center`.

**Estado:** versión 1.0.0 con paquete instalable armado por `Crear-paquete.ps1`.

**Objetivo (supuesto):** validar el paquete con uso real y pulir lo que salga. El objetivo quedó en blanco al crear el seguimiento.

**Pruebas:** una prueba al terminar cada fase.

## Fases (T001–T012 en el dashboard)
1. Uso diario del papá
2. Instalación y actualizaciones fáciles
3. Actualizar con su propio Claude
4. Pulido y entrega

## Revisión del uso diario con Excel y Claude Code (T001)

Mejoras priorizadas, de más a menos valiosa:

1. **El asistente no sabe de Excel.** `AiModule` no tiene herramientas de Excel: para "suma la columna B" o "ponle negrita al encabezado" tiene que teclear a ciegas. Agregar herramientas `excel_read` (rango), `excel_write` (celda o rango) y `excel_action`, apoyadas en `ExcelModule`. *(primera mejora → tarea T013)*
2. **Los ejemplos del asistente son de la versión de Chacho.** `AiScreen.kt` ofrece "Anota una idea…", pero el buzón de ideas se quitó; además faltan ejemplos de Excel. Cambiarlos por órdenes que sí existen (Excel, abrir archivo, volumen).
3. **Excel: seleccionar rangos desde el teléfono.** Hoy solo se toca una celda; falta arrastrar o elegir un rango (A1:B10) para copiar, formatear o sumar.
4. **Excel: cuadrícula pequeña (12×5 fijo).** Permitir desplazarla o ampliarla, y escribir en una celda tocada sin ir a otra sección.
5. **Excel: más toques de los necesarios.** La pantalla es una lista larga; agrupar acciones (Editar, Formato, Hoja) y dejar fijas las más usadas (Guardar, Deshacer).
6. **Asistente: acceso rápido a Excel.** Mostrar "Excel" como módulo favorito por defecto en el inicio (hoy el valor por defecto es Touchpad/Puntero/Media/YouTube/Ventanas/IA).
7. **Claude Code:** que el asistente muestre cuándo está esperando aprobación en el teléfono (PowerShell) de forma más visible, y poder reintentar el último mensaje.
