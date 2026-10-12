# Plan de pruebas controladas (sin tocar tu PC Hub)

Objetivo: probar la versión nueva en sesiones cortas (abres, pruebas, cierras y te olvidas), sin cambiar nada de tu PC Hub de siempre.

## Qué queda protegido

| Lo tuyo | Cómo se protege |
|---|---|
| Configuración, PIN y teléfonos vinculados (`%APPDATA%\PcHub`) | La sesión usa otra carpeta: `%APPDATA%\PcHub-Prueba`. Nunca lee ni escribe la tuya. |
| Programa instalado (`%LOCALAPPDATA%\Programs\PcHub`) | El modo prueba no se instala, no ofrece actualizar y no reemplaza nada. |
| "Iniciar con Windows" y entrada en Aplicaciones | El modo prueba no los toca. |
| Tu PC Hub abierto | Usa las mismas puertas de red, así que `Probar-version.ps1` lo cierra durante la sesión y **lo vuelve a abrir solo al terminar**. |
| Tu app del teléfono | La prueba usa una app aparte: **PC Hub Prueba** (`com.chacho.pchub.prueba`). Conviven; no se reemplazan. |

## Cómo es una sesión

1. En PowerShell, en la carpeta del proyecto: `.\Probar-version.ps1` (agrega `-Limpio` para empezar de cero, `-Apk` para armar la app del teléfono).
2. Se abre PC Hub en modo PRUEBA (ícono junto al reloj, dice "PRUEBA" al pasar el mouse). Doble clic ahí para ver el PIN.
3. En el teléfono abre **PC Hub Prueba**, elige el PC y escribe el PIN.
4. Haz las pruebas de abajo.
5. Clic derecho en el ícono -> **Salir**. La ventana de PowerShell reabre tu PC Hub y dice "Sesión terminada". Listo, te despreocupas.

Si algo se cuelga: cierra la ventana de PowerShell o ejecuta `Get-Process PcHub.Agent | Stop-Process` y abre tu PC Hub desde el menú Inicio. Tus datos no se tocaron.

## Primera vez (una sola vez)

- `.\Probar-version.ps1 -Apk` arma `PcHub-Prueba.apk`. Pásalo al teléfono e instálalo. Si es un Samsung, mira la sección "SI TU TELÉFONO ES SAMSUNG GALAXY" de `LEEME-PAPA.txt`.
- Para quitarlo cuando quieras: Ajustes del teléfono -> Aplicaciones -> PC Hub Prueba -> Desinstalar. Y en el PC, borrar la carpeta `%APPDATA%\PcHub-Prueba`.

## Sesiones sugeridas (15 minutos cada una)

**Sesión 1 – Conexión y estilo.** Conectar con el PIN, mirar Inicio y recorrer Excel, Asistente y Ajustes. Revisar el estilo Samsung (P007) y que nada se vea cortado en la pantalla grande.

**Sesión 2 – Excel.** Abrir un libro copia (no el importante) y probar tocar celdas, escribir número y fórmula, copiar/pegar, saltar al borde, filas, formato %/$, deshacer y guardar (P001).

**Sesión 3 – Asistente.** Con el libro copia abierto: "Suma la columna B", "Ponle negrita a la fila 1". Probar también pedir algo de música o abrir una app.

**Sesión 4 – Tu rutina real.** Usar la app como en un día normal (con copias de tus archivos) y anotar lo que estorba.

**Qué NO se prueba con este modo (y cómo hacerlo):** instalar y actualizar (el instalador sí modifica el PC). Para eso usa otro PC o una cuenta de Windows de prueba, siguiendo P002 y P004.

## Cuando termines una sesión

Dime en una línea qué viste (qué falló, qué te confundió). Si quieres que lo arregle, pídemelo; cada hallazgo se convierte en una tarea y la siguiente sesión empieza con `-Limpio`.
