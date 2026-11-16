# DSH Desktop

> The [DeepSeek Harness](https://github.com/deepseek-ai) web UI in one folder: the Node runtime travels with it and **dsh is downloaded on first launch**, so double-clicking `dsh-desktop.exe` is enough. The target machine needs no Node.js, no administrator rights, and nothing is written to the registry.

English | [中文](README.md) · version **1.0.0**

## What it is

`dsh web` needs a Node server on the machine and a browser to open it; intranet machines usually have neither Node nor a way to install software.

DSH Desktop keeps all three pieces together in a portable folder:

1. A Node runtime travels with it; the **first launch installs the npm `latest` tag of `@deepseek-ai/dsh` with pnpm**, and later launches start the server directly
2. WebView2 comes from the system when present, otherwise a copy is downloaded into this folder — no system component is installed
3. A drawn window reads the tokenised address the server prints and navigates straight to it, so **the first window is already signed in**

## Features

- **Small and out of the box**: unzip → double-click → the first launch fetches dsh → you are in a session; the package is about 40MB
- **Immersive title bar**: no system frame; a 40px drawn bar that samples the page's real background colours and the seam between its rail and content, and **follows a theme flip immediately** (event driven, no polling)
- **Shaped like DSH**: settings, about, update and menus use DSH's palette, radii, row heights and scroll bars; the wording follows the page's language (`zh` `en` `ja` `es` `fr` `de` `ru` `pt`)
- **Two roles**: bundled server by default; `server=0` makes it a thin client that only opens `url=`
- **dsh lives outside the package**: fetched on first launch and replaced in place by "Check dsh updates", rolling back if the swap fails
- **Truly portable**: settings, credentials, sessions and plugins live in this folder's `data\`; deleting the folder uninstalls it
- **No orphaned processes**: a Job Object ties the server to the launcher, so it ends with the window even after a crash
- **Single-instance guard**: one data home, one server, so two processes never write the same session history
- **Diagnosable**: `dsh-desktop.log` records each launch, the server output, updates and downloads

## Quick start

```
1. Unzip the whole folder (e.g. D:\DSH Desktop\)
2. Double-click dsh-desktop.exe
3. Settings → Models, enter an API key (or sign in with a DeepSeek account)
```

The first launch downloads dsh from the npm `latest` tag (about 490MB, including the Office conversion engine and the speech model) with a progress bar (percentage) and a byte counter on the splash, then initialises the workspace and profile; later launches take 1–2 seconds. A failed download is explained in the window and the log.


- **Client only**: gear → turn off "start the bundled dsh web server" → enter the address → save and restart
- **Another project**: put it under `workspace\`, or change the workspace folder in the settings

## Folder layout

```
dsh-desktop.exe              launcher (WinForms + WebView2; sources under src\)
dsh-desktop.ini              configuration
runtime\                     Node runtime
app\node_modules\            dsh and its dependencies (fetched on first launch)
app\.dsh-desktop-prune.txt   payload to drop after an install
webview2\                    fixed-version engine (absent by default; downloaded when needed)
data\                        DSH_HOME: settings, credentials, sessions, plugins
workspace\                   default agent workspace
profile\                     browser data (cookies, login state)
dsh-desktop.log              run log
```

## Where WebView2 comes from

The default package carries **no** engine (that would add 500MB). On launch it looks for, in order:

1. `webview2\` next to the exe (downloaded earlier, or a `-WithWebView2` build)
2. the WebView2 runtime installed on the machine
3. neither → it explains, downloads the version named by `webview2version` from NuGet and extracts it into `webview2\`

The download only writes inside this folder and installs nothing system-wide; cancelling it exits.

To exercise that path on a machine that does have WebView2, set `DSH_DESKTOP_NO_WEBVIEW2=1` before launching: the launcher pretends no runtime exists and walks the whole detect-fail, download, boot-from-download flow.

## Build

The repository **holds source only**. The default package carries neither dsh (fetched on first launch) nor the WebView2 engine (the system runtime is preferred).

Three products, pick what you need:

| Variant | Command | Output | Size (folder / zip) | Notes |
|---|---|---|---|---|
| **slim** (default) | `build.ps1 -Zip` | `dsh-desktop-slim-win64.zip` | 113 MB / **39 MB** | no dsh; the first launch downloads it (about 490MB) |
| **with dsh** | `build.ps1 -Variant dsh -Zip` | `dsh-desktop-win64.zip` | 609 MB / **203 MB** | dsh bundled (npm `latest`) and pruned, works offline out of the box |
| **full** | `build.ps1 -Variant full -Zip` | `dsh-desktop-full-win64.zip` | 1182 MB / **466 MB** | dsh (npm `latest`) **and the WebView2 runtime** bundled, with **no pruning**; runs with no network at all |

Build all three in one run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -All
```

Other switches:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Variant dsh -Force        # rebuild one variant (still npm latest)
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Variant dsh -DshVersion 0.2.2-rc.3   # pin a version
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Variant dsh -WithWebView2 # add the engine to the with-dsh package too
```

Each variant lands in `build\<variant>\`; `-Zip` also writes the matching archive under `build\`.

What it does: find a local Node (≥24) or download the official 24.x archive → prepare pnpm → pull the WebView2 SDK from NuGet → compile the launcher with the in-box `csc.exe` → assemble a ready-to-copy folder. dsh is **not** installed by default (a ~40MB package); pass `-WithDsh` to bundle it into `app\` (about 400MB, for fully offline use).

No Visual Studio, .NET SDK or NuGet client is needed. An incremental build only recompiles the executable (about a second); Node and SDK downloads stay in `build\.cache`, so later builds work offline.

### Pruning

Only two kinds of payload go: **binaries this machine cannot run**, and **debug files normal use never reads**.

| Removed | Size | Effect |
|---|---|---|
| WASM engines (`libreoffice-kit-wasm`, `sharp-wasm32`, ...) | about 145MB | None — the native engine is used here |
| Other platforms and architectures (macOS, Linux, `win32-arm64`, `win32-ia32`) | about 12MB | None — this package runs on Windows x64 only |
| `*.map`, `*.pdb` | about 121MB | None — source maps only serve browser devtools, debug symbols only a native stack walker |

**Kept**: `libreoffice-kit-win32-x64` (Office conversion), `sherpa-onnx-win-x64` (voice input) and every Windows x64 native module — about 490MB in total.

Pass `-Full` to skip all pruning.

## Configuration

Both routes are equivalent: the **settings menu** (gear, `Ctrl+,`) or editing `dsh-desktop.ini` directly. The menu writes the same file back and keeps its comments.

| Key | Default | Meaning |
|---|---|---|
| `server` | `1` | `1` starts the bundled `dsh web`; `0` is client only and requires `url` |
| `url` | `http://127.0.0.1:3080/` | Address used when `server=0`; may carry `?token=` the first time |
| `host` | `127.0.0.1` | Listen address (server mode) |
| `port` | `0` | Listen port; `0` picks a free port on every launch |
| `workspace` | `workspace` | Agent workspace root |
| `data` | `data` | DSH_HOME (settings, credentials, sessions, plugins) |
| `profile` | `profile` | Browser data directory |
| `runtime` | `runtime` | Node runtime directory |
| `app` | `app` | Bundled dsh directory |
| `args` | empty | Extra arguments for `dsh web` |
| `toolbar` | `0` | `1` shows the address bar, back/forward and zoom |
| `zoom` | `1.0` | Initial zoom factor |
| `icon` | `default` | `default` / `auto` / `light` / `dark` |
| `webview2version` | `157.0.4341.46` | Version downloaded when the machine has no runtime and the folder has no `webview2\`. Ignored when a system runtime exists |
| `closetotray` | `0` | `1` = the close button minimises to tray (the server keeps running) |
| `telemetry` | `0` | Send anonymous usage telemetry |
| `startupseconds` | `120` | Seconds to wait for the server |

Relative paths resolve against the executable's folder; a deleted file is recreated.

### Command line

Every ini key has an equivalent argument (the command line wins over the file), which is handy for scripts, shortcuts or trying a value once:

| Argument | ini key | Meaning |
|---|---|---|
| `--server=0` / `--server=1` | `server` | Turn the bundled server off or on |
| `--client` | `server=0` | The same, as a short switch |
| `--url=<address>` | `url` | Address used in client mode |
| `--host=<address>` | `host` | Listen address |
| `--port=<port>` | `port` | Listen port, `0` for automatic |
| `--workspace=<dir>` | `workspace` | Workspace root |
| `--data=<dir>` | `data` | DSH_HOME |
| `--profile=<dir>` | `profile` | Browser data directory |
| `--runtime=<dir>` | `runtime` | Node runtime directory |
| `--app=<dir>` | `app` | Bundled dsh directory |
| `--args="<args>"` | `args` | Extra arguments for `dsh web` |
| `--toolbar` / `--toolbar=0` | `toolbar` | Show or hide the address bar |
| `--zoom=<factor>` | `zoom` | Initial zoom, e.g. `--zoom=1.2` |
| `--icon=<style>` | `icon` | `default` / `auto` / `light` / `dark` |
| `--webview2version=<version>` | `webview2version` | Version to download for this launch |
| `--closetotray=0` / `=1` | `closetotray` | What the close button does |
| `--telemetry=0` / `=1` | `telemetry` | Anonymous usage telemetry |
| `--startupseconds=<n>` | `startupseconds` | Startup timeout |

Utility arguments (they do not change the configuration):

| Argument | Effect |
|---|---|
| `--selftest` | Start the server, load the page, write `selftest-result.json` and exit |
| `--update-dsh=<version>` | Update the bundled dsh in place, then exit |
| `--download-webview2` | Force the WebView2 download path |
| `--bare` | Start without any patch overlay |

Each launch writes one `config: ...` line to the log with every resolved value and expanded path, so you can confirm an argument took effect.

## The interface

- **Settings** (gear / `Ctrl+,`): Startup (server switch, address, listen, port, timeout), Appearance (icon, zoom, address bar, tray), Folders, Advanced (extra arguments, telemetry). "Save and restart" applies immediately
- **`⋯` menu**: settings, check dsh updates, open in browser, copy address, developer tools, open workspace / data folder / config file, about
- **Tray menu** (with `closetotray=1`): show window, settings, about, quit
- **About**: launcher version, dsh version, Node, WebView2, mode, address, folders, log path, with a copy button

## Updating dsh

`⋯` → "Check dsh updates":

- **Channel choice**: `latest` (release stream) or `next` (previews); changing it re-queries that tag immediately
- **Force**: when on, the same or an older version may be installed; by default a downgrade is never offered
- "Update now" stops the bundled server first, installs a new tree into `data\update-staging` with pnpm, verifies the version, then swaps it into `app\node_modules`; the swap retries and **rolls back** on failure
- afterwards the same prune list as the build is re-applied, so an updated folder keeps a fresh build's size
- a themed prompt offers the restart

From a script: `dsh-desktop.exe --update-dsh=0.2.2-rc.3` (runs and exits).

## Icons

| Value | Preview | Meaning |
|---|---|---|
| `default` | ![default](docs/ic-default.png) | Official DSH artwork (rounded backdrop + whale), the default |
| `auto` | ![auto](docs/ic-auto.png) | Follow the system: dark whale on light, white on dark |
| `light` | ![light](docs/ic-light.png) | Always the dark whale, for light bars |
| `dark` | ![dark](docs/ic-dark.png) | Always the white whale, for dark bars |

The executable's static file icon is always `default`; only the window and taskbar icon follow the setting.

## About this project

- icons come from DSH's official artwork, used only to identify this tool
- not affiliated with DeepSeek
- the bundled `@deepseek-ai/dsh` and its dependencies keep their own licenses

## License

[MIT](LICENSE)
