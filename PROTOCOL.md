# Protocolo PC Hub (v1)

Transporte: **WebSocket** en `ws://<ip-del-pc>:47800/ws` (red local).
Descubrimiento: el teléfono envía el texto `PCHUB?` por **UDP broadcast** al puerto `47801`; cada agente responde
con `{"name":"MI-PC","port":47800,"v":1}`.
Canal rápido de mouse: **UDP** al puerto `47802` (opcional, ver más abajo).

Todos los mensajes son JSON de una línea.

## Teléfono → PC

```json
{"t":"req","id":7,"m":"media.cmd","p":{"a":"playpause"}}
```

- `id` es opcional. Si no viene, el PC no responde (se usa para mensajes muy frecuentes como `mouse.move`).
- `m` = `módulo.acción`, `p` = parámetros.

## PC → Teléfono

Respuesta a una petición:

```json
{"t":"res","id":7,"ok":true,"d":{...}}
{"t":"res","id":7,"ok":false,"err":"mensaje"}
```

Evento (el PC avisa por su cuenta):

```json
{"t":"evt","e":"ctx.changed","d":{"process":"chrome","title":"YouTube Music","module":"media"}}
```

## Autenticación

Hasta autenticarse, solo se aceptan `auth.*`.

| Método | Parámetros | Respuesta |
|---|---|---|
| `auth.pair` | `pin`, `device` | `token`, `pcName` |
| `auth.hello` | `token` | `pcName` |

El PIN de 6 dígitos aparece en el menú del ícono de bandeja. Tras 5 intentos fallidos se genera uno nuevo.

## Métodos

| Método | Parámetros | Respuesta |
|---|---|---|
| `mouse.move` | `dx`, `dy` (enteros, relativos) | — |
| `mouse.click` | `b` = `left`/`right`/`middle`, `double` (bool) | — |
| `mouse.down` / `mouse.up` | `b` | — |
| `mouse.scroll` | `dy`, `dx` (120 = un "clic" de rueda) | — |
| `mouse.abs` | `x`, `y` (0..1 sobre la pantalla principal) | — |
| `input.udp` | — | `{port, key}`: clave hex de 16 dígitos para el canal UDP de mouse |
| `kb.text` | `text` | — |
| `kb.key` | `combo` (ej. `ctrl+shift+esc`), `repeat` | — |
| `kb.down` / `kb.up` | `key` (ej. `alt`) | — |
| `media.get` | — | estado de media (ver evento) |
| `media.cmd` | `a` = `playpause`/`play`/`pause`/`next`/`prev`/`seek`, `pos` (segundos, para seek) | — |
| `vol.get` | — | `level` (0-1), `muted`, `device` |
| `vol.set` | `level` (0-1) | — |
| `vol.mute` | `muted` (bool) | — |
| `vol.apps` | — | `apps: [{pid,name,level,muted}]` |
| `vol.app.set` | `pid`, `level`, `muted` | — |
| `win.list` | — | `windows: [{h,title,proc,icon,active}]` |
| `win.focus` / `win.close` / `win.minimize` | `h` | — |
| `apps.list` | — | `apps: [{id,name,icon}]` |
| `apps.launch` | `id` | — |
| `sys.action` | `a` = `lock`/`sleep`/`monitor_off`/`shutdown`/`restart`/`cancel_shutdown` | — |
| `clip.get` | — | `text` |
| `clip.set` | `text` | — |
| `cmd.list` | — | `commands: [{id,name,icon,type,confirm}]` |
| `cmd.run` | `id` | `output` |
| `ctx.get` | — | contexto actual |
| `ai.ask` | `text` | `reply`, `actions` |
| `ai.reset` | — | — |
| `ai.confirm` | `id`, `ok` (bool) | — |
| `app.info` | — | `version`, `installed`, `runningInstalled`, `startup`, `ai` |
| `excel.status` | — | `installed`, `running`, `workbook?`, `saved`, `sheet`, `sheets`, `cell`, `value`, `formula`, `selection` |
| `excel.grid` | `rows?` (12), `cols?` (5) | `firstRow`, `firstCol`, `cols` (letras), `rows` (texto de cada celda), `activeRow`, `activeCol` |
| `excel.select` | `cell` ("B7") o `dRow`, `dCol` (relativo a la celda activa) | estado |
| `excel.write` | `value` (texto, número o fórmula con `=`), `next` = `down`/`right`/`none` | estado |
| `excel.action` | `a` = `save` `undo` `clear` `bold` `autosum` `filter` `next_sheet` `prev_sheet` `new_sheet` `copy` `cut` `paste` `home` `jump_up` `jump_down` `jump_left` `jump_right` (como Ctrl+flecha) `insert_row` `delete_row` `autofit` `percent` `money` `number` | estado |
| `excel.sheet` | `name` | estado |
| `excel.files` | — | `files: [{path,name,folder,modified}]` (Recientes, Documentos, Escritorio y Descargas) |
| `excel.open` | `path` (.xlsx/.xlsm/.xls/.csv) | `opening` |
| `excel.new` | — | estado (abre Excel con un libro en blanco) |
| `app.startup` | `enabled` (bool) | igual que `app.info` |
| `yt.search` | `q`, `music` (bool) | `results: [{id,title,channel,duration,views,thumb}]` |
| `yt.play` | `id`, `list` (opcional; sin `id` = la lista completa), `music` (bool). Con `music` se abre en la app (PWA) de YouTube Music y, si hay un video de YouTube sonando, se pausa | `message` |
| `web.open` | `url` (http/https) — pestaña nueva en el navegador predeterminado de Windows | — |
| `term.list` | — | `tabs: [{id,title,running,onPc}]` |
| `term.open` | `cols?`, `rows?` | la pestaña nueva (PowerShell en una pseudoconsola, en la carpeta del usuario) |
| `term.attach` | `id`, `cols?`, `rows?` | `data` (base64 con lo último que mostró), `running`, `onPc`. Desde ahí este teléfono recibe `term.out` de esa pestaña |
| `term.detach` | — | deja de recibir `term.out` |
| `term.input` | `id`, `data` (teclas tal cual las manda xterm.js: texto, `\r`, secuencias de flechas, Ctrl+C = `\u0003`) | — |
| `term.resize` | `id`, `cols`, `rows` | — |
| `term.restart` / `term.close` | `id` | — |
| `term.rename` | `id`, `title` | — |
| `term.showPc` | `id`, `show` (bool): abre o cierra en el PC una ventana con la misma sesión | la pestaña |
| `screen.preview` | — | `image` (JPEG base64 de la pantalla principal, máx. 1280 px de ancho), `width`, `height` (px reales) |
| `screen.capture` | `mode` (`full`/`window`/`region`), y para `region`: `x`, `y`, `w`, `h` (fracciones 0..1 de la pantalla) — la imagen queda en el portapapeles del PC | `message` |

`icon` siempre es un PNG en base64 (32 px).

## Canal rápido de mouse (UDP)

Tras autenticarse, el teléfono pide `input.udp` y manda el movimiento del mouse en paquetes UDP de 15 bytes
(little-endian) al `port` recibido. Un paquete perdido no frena a los siguientes, a diferencia del WebSocket (TCP).
La clave deja de valer al cerrarse el WebSocket. Si el canal no está disponible, todo se manda por WebSocket.

| Bytes | Contenido |
|---|---|
| 0–7 | `key` (uint64) |
| 8 | tipo: `1` mover (int16 dx, int16 dy) · `2` scroll (int16 dy, int16 dx) · `3` absoluto (uint16 x, uint16 y; 0..65535) |
| 9–10 | secuencia (uint16); las posiciones absolutas más viejas que la última aplicada se descartan |
| 11–14 | los dos valores del tipo |

## Eventos

| Evento | Datos |
|---|---|
| `ctx.changed` | `process`, `title`, `module` (o `null`) |
| `media.state` | `title`, `artist`, `album`, `app`, `status`, `position`, `duration`, `art` (JPEG base64 o `null`) |
| `input.focus` | `editable` (bool), `password` (bool), `name` |
| `ai.step` | `text` (lo que la IA está haciendo) |
| `ai.confirm_req` | `id`, `title`, `detail` — el teléfono debe responder con `ai.confirm` |
| `term.out` | `id`, `data` (base64; solo a los teléfonos que hicieron `term.attach` de esa pestaña) |
| `term.state` | `id`, `title`, `running`, `onPc` |
| `term.closed` | `id` |

## Cómo agregar un módulo nuevo

1. **PC:** crea `Modules/MiModulo.cs` y registra sus métodos en `Hub.RegisterRoutes()` (`Hub.cs`).
2. **Android:** crea `ui/MiModuloScreen.kt`, agrégalo al enum `Screen` en `Navigation.kt` (aparece solo en Módulos) y su `when` en `AppRoot.kt`.
3. Si debe abrirse por contexto, agrega una regla en `contextRules` del `config.json` con `"module": "mimodulo"`.
