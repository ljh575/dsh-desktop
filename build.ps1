<#
.SYNOPSIS
    Builds the DSH Desktop portable folder from source.

.DESCRIPTION
    Assembles a self-contained DeepSeek Harness desktop folder:

      * the bundled Node.js runtime (node.exe only, ~90MB)
      * @deepseek-ai/dsh and all of its dependencies (~360MB after pruning)
      * the fixed-version Microsoft WebView2 runtime (~510MB)
      * the WebView2 SDK assemblies and the dsh-desktop.exe launcher

    Nothing else is required on the target machine: no Node.js, no npm, no
    WebView2 runtime, no administrator rights. Copy the folder, double-click
    dsh-desktop.exe.

    The build is incremental. Downloads land in build/.cache and survive, the
    npm dependency tree is only reinstalled when -Force or a version change
    asks for it, and the launcher is recompiled in about a second. Pass -Force
    to rebuild everything from scratch.

    Requires Windows + .NET Framework 4.x (csc.exe ships with Windows) and
    network access on the first run. The produced folder runs offline.

.PARAMETER DshVersion
    The @deepseek-ai/dsh version to bundle. Defaults to the npm `latest` tag.

.PARAMETER Zip
    Also produce a single distributable zip next to the output folder.

.PARAMETER Full
    Keep every optional dependency. The default build drops the packages that
    only serve features a portable desktop install never reaches (LibreOffice
    engines, the SenseVoice speech model, non-Windows native binaries), which
    saves about 400MB.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Force -Full
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$CacheDir,
    [string]$DshVersion,
    [string]$NodeVersion = '24.22.3',
    [string]$WebView2Version = '157.0.4341.46',
    [string]$SdkVersion = '1.0.3967.48',
    [switch]$Zip,
    [switch]$Force,
    # slim = no dsh (the launcher installs it on first launch), dsh = dsh bundled and
    # pruned, full = dsh bundled with every optional dependency (no pruning).
    [ValidateSet('slim', 'dsh', 'full')]
    [string]$Variant,
    [switch]$Full,
    # Bundle @deepseek-ai/dsh inside app\ (~360MB) instead of letting the launcher
    # install it on first launch.
    [switch]$WithDsh,
    # Build all three variants in one run, each with its own folder and zip.
    [switch]$All,
    # Bundle the fixed-version WebView2 runtime (~510MB) instead of relying on
    # the one installed on the machine. Off by default: the launcher prefers the
    # system runtime and downloads a copy into this folder only when there is none.
    [switch]$WithWebView2
)

$ErrorActionPreference = 'Stop'

# The bundled variants carry whatever npm publishes as `latest`, so all three
# packages agree with what a fresh install would fetch.
if (-not $DshVersion) {
    $DshVersion = '0.2.2-rc.1'
    try {
        $probe = Invoke-WebRequest -Uri 'https://registry.npmjs.org/@deepseek-ai%2Fdsh' -UseBasicParsing -TimeoutSec 30
        $hit = [regex]::Match($probe.Content, '"dist-tags"\s*:\s*\{[^}]*"latest"\s*:\s*"([^"]+)"')
        if ($hit.Success) { $DshVersion = $hit.Groups[1].Value }
    }
    catch { Write-Warning "could not read the npm latest tag; bundling $DshVersion" }
}
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# $PSScriptRoot is empty inside param() defaults on Windows PowerShell 5.1,
# so the script root is resolved here instead.
$ScriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
# The switches predate the variant names; fold them in so both spellings work.
if (-not $Variant) {
    if ($Full) { $Variant = 'full' }
    elseif ($WithDsh) { $Variant = 'dsh' }
    else { $Variant = 'slim' }
}
if ($Full) { $Variant = 'full' }
if ($WithDsh -and -not $Full) { $Variant = 'dsh' }
# The full package is the offline one, so it carries the WebView2 engine as well.
if ($Variant -eq 'full') { $WithWebView2 = $true }
# -All drives the three variants through this same script, one folder and zip each.
if ($All) {
    foreach ($each in @('slim', 'dsh', 'full')) {
        Write-Host ''
        Write-Host "===== building the $each package =====" -ForegroundColor Cyan
        & (Join-Path $ScriptRoot 'build.ps1') -Variant $each -Zip `
            -Force:$Force -DshVersion $DshVersion -NodeVersion $NodeVersion `
            -WebView2Version $WebView2Version -SdkVersion $SdkVersion -WithWebView2:($WithWebView2 -or $each -eq 'full')
        if ($LASTEXITCODE) { throw "building the $each package failed" }
    }
    Write-Host ''
    Write-Host 'Three packages built:' -ForegroundColor Green
    Write-Host '  build/dsh-desktop-slim-win64.zip   (no dsh, first launch downloads it)'
    Write-Host '  build/dsh-desktop-win64.zip        (dsh bundled, pruned)'
    Write-Host '  build/dsh-desktop-full-win64.zip   (dsh and WebView2 bundled, nothing pruned)'
    return
}

if (-not $OutDir) { $OutDir = Join-Path $ScriptRoot "build/$Variant" }
if (-not $CacheDir) { $CacheDir = Join-Path $ScriptRoot 'build/.cache' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$CacheDir = [System.IO.Path]::GetFullPath($CacheDir)
$WorkDir = Join-Path (Split-Path -Parent $OutDir) 'work'

$RuntimeMarker = "$WebView2Version.manifest"

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note([string]$Message) { Write-Host "    $Message" -ForegroundColor DarkGray }
function Write-Ok([string]$Message) { Write-Host "    $Message" -ForegroundColor Green }

function Test-Archive([string]$Path) {
    # nupkg/zip and npm .tgz both arrive here; each has its own validity check.
    if ($Path.EndsWith('.tgz') -or $Path.EndsWith('.tar.gz')) {
        & tar.exe -tzf $Path *>$null
        return $LASTEXITCODE -eq 0
    }
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
        $zip.Dispose()
        return $true
    }
    catch { return $false }
}

function Get-RemoteSize([string]$Url) {
    $head = & curl.exe -s -I -L --max-time 60 $Url 2>$null | Select-String -Pattern '^Content-Length:\s*(\d+)'
    if ($head) {
        $value = [int64]$head[-1].Matches[0].Groups[1].Value
        # A redirect or a HEAD that answers 0 is not a usable expected size.
        if ($value -gt 0) { return $value }
    }
    return $null
}

# Downloads get truncated behind proxies, so resume until the size matches.
# The cache is always reused when valid; -Force only rebuilds the output folder.
function Get-Cached([string]$Url, [string]$Destination, [string]$Label) {
    if (Test-Path $Destination) {
        $size = (Get-Item $Destination).Length
        $expected = Get-RemoteSize $Url
        if ($expected) {
            if ($size -eq $expected) {
                Write-Note "cached: $Label"
                return
            }
        }
        elseif (Test-Archive $Destination) {
            # No usable size from the server: a readable archive is good enough,
            # which keeps an offline rebuild working.
            Write-Note "cached (offline): $Label"
            return
        }
    }
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw 'curl.exe is required (it ships with Windows 10 1803+).'
    }
    $expected = Get-RemoteSize $Url
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Remove-Item $Destination -Force -ErrorAction SilentlyContinue
    for ($attempt = 1; $attempt -le 25; $attempt++) {
        & curl.exe -s -L -C - --max-time 300 -o $Destination $Url 2>$null | Out-Null
        $size = if (Test-Path $Destination) { (Get-Item $Destination).Length } else { 0 }
        Write-Note ("{0}: {1:N1} MB{2}" -f $Label, ($size / 1MB), $(if ($expected) { " / $([math]::Round($expected/1MB,1)) MB" } else { '' }))
        if (-not $expected -or $size -ge $expected) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($expected -and (Get-Item $Destination).Length -ne $expected) {
        throw "download incomplete: $Label"
    }
    if (-not (Test-Archive $Destination)) { throw "not a valid archive: $Label" }
}

function Expand-NuGet([string]$Package, [string]$Destination) {
    Remove-Item $Destination -Recurse -Force -ErrorAction SilentlyContinue
    $zip = [System.IO.Path]::ChangeExtension($Package, '.zip')
    Copy-Item $Package $zip -Force
    Expand-Archive -Path $zip -DestinationPath $Destination -Force
}

function Test-WebView2([string]$Dir) {
    if (-not (Test-Path $Dir)) { return $false }
    foreach ($file in 'msedgewebview2.exe', 'msedge.dll', $RuntimeMarker) {
        if (-not (Test-Path (Join-Path $Dir $file))) { return $false }
    }
    return $true
}

function Test-Sdk([string]$Dir) {
    foreach ($file in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') {
        if (-not (Test-Path (Join-Path $Dir $file))) { return $false }
    }
    return $true
}

function Test-App([string]$Dir, [string]$Version) {
    $entry = Join-Path $Dir 'node_modules/@deepseek-ai/dsh/lib/bin.js'
    $manifest = Join-Path $Dir 'node_modules/@deepseek-ai/dsh/package.json'
    if (-not (Test-Path $entry)) { return $false }
    if (-not (Test-Path $manifest)) { return $false }
    try {
        $actual = (Get-Content $manifest -Raw | ConvertFrom-Json).version
        return $actual -eq $Version
    }
    catch { return $false }
}

function Find-Node([string]$CacheRoot, [string]$Version) {
    # Prefer a node that is already on this machine so a source build does not
    # need the 36MB download; fall back to the official archive.
    $candidates = @()
    if ($env:DSH_PRIMARY_NODE) { $candidates += $env:DSH_PRIMARY_NODE }
    $candidates += @(
        (Join-Path $env:USERPROFILE '.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\node\bin\node.exe'),
        (Join-Path $env:LOCALAPPDATA 'deepseek-harness\node\node.exe')
    )
    $onPath = Get-Command node -ErrorAction SilentlyContinue
    if ($onPath -and $onPath.Source) { $candidates += $onPath.Source }

    foreach ($candidate in $candidates) {
        try {
            if (-not $candidate -or -not (Test-Path $candidate)) { continue }
            $text = (& $candidate --version 2>$null | Out-String).Trim()
            if ($text -match '^v(\d+)\.(\d+)\.(\d+)$') {
                $major = [int]$matches[1]
                if ($major -ge 24) { return $candidate }
            }
        }
        catch { }
    }

    $zip = Join-Path $CacheRoot "node-v$Version-win-x64.zip"
    Get-Cached "https://nodejs.org/dist/v$Version/node-v$Version-win-x64.zip" $zip "node $Version"
    $expanded = Join-Path $CacheRoot "node-v$Version"
    $marker = Join-Path $expanded "node-v$Version-win-x64/node.exe"
    if (-not (Test-Path $marker)) {
        Remove-Item $expanded -Recurse -Force -ErrorAction SilentlyContinue
        Expand-Archive -Path $zip -DestinationPath $expanded -Force
    }
    return $marker
}

function Install-Pnpm([string]$NodeExe, [string]$Version) {
    # DSH itself ships no package manager, and the Node runtime bundled with the
    # Desktop app ships no npm either, so pnpm is fetched on its own: a 5MB npm
    # tarball extracted with the tar.exe that ships with Windows.
    $prefix = Join-Path $CacheDir "pnpm-$Version"
    # An npm tarball extracts into a single `package/` folder.
    $entry = Join-Path $prefix 'package/bin/pnpm.mjs'
    if (Test-Path $entry) { return $entry }

    Write-Step "Fetching pnpm $Version"
    Remove-Item $prefix -Recurse -Force -ErrorAction SilentlyContinue
    $tgz = Join-Path $CacheDir "pnpm-$Version.tgz"
    Get-Cached "https://registry.npmjs.org/pnpm/-/pnpm-$Version.tgz" $tgz "pnpm $Version"

    New-Item -ItemType Directory -Force -Path $prefix | Out-Null
    & tar.exe -xzf $tgz -C $prefix 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $entry)) {
        # Older tar.exe that cannot read gzip: fall back to npm when it exists.
        Remove-Item $prefix -Recurse -Force -ErrorAction SilentlyContinue
        $npmCli = Join-Path (Split-Path -Parent $NodeExe) 'node_modules/npm/bin/npm-cli.js'
        $npm = Get-Command npm -ErrorAction SilentlyContinue
        if (Test-Path $npmCli) {
            $env:npm_config_ignore_scripts = 'true'
            & $NodeExe $npmCli install "pnpm@$Version" --prefix $prefix --no-audit --no-fund --loglevel=error 2>&1 |
                Select-Object -Last 3 | ForEach-Object { Write-Note $_ }
            $entry = Join-Path $prefix 'node_modules/pnpm/bin/pnpm.mjs'
        }
        elseif ($npm -and $npm.Source) {
            & $npm.Source install "pnpm@$Version" --prefix $prefix --no-audit --no-fund 2>&1 |
                Select-Object -Last 3 | ForEach-Object { Write-Note $_ }
            $entry = Join-Path $prefix 'node_modules/pnpm/bin/pnpm.mjs'
        }
    }
    if (-not (Test-Path $entry)) { throw "could not prepare pnpm at $entry" }
    $script:PnpmEntry = $entry
    return $entry
}

function Install-App([string]$Destination, [string]$Version, [string]$NodeExe) {
    $pnpmEntry = Install-Pnpm $NodeExe '11.28.5'

    Remove-Item $Destination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    # A hoisted node_modules has no symlinks or junctions, which matters when the
    # folder is copied to a machine, a network share, or a FAT32 USB stick.
    # autoInstallPeers stays on because the dsh plugin tree declares cordis
    # packages as peers and would not boot without them.
    $manifest = @{ name = 'dsh-desktop-runtime'; private = $true } | ConvertTo-Json
    Set-Content -Path (Join-Path $Destination 'package.json') -Value $manifest -Encoding UTF8
    $workspace = @(
        'packages:',
        '  - .',
        'nodeLinker: hoisted',
        'autoInstallPeers: true'
    ) -join "`n"
    Set-Content -Path (Join-Path $Destination 'pnpm-workspace.yaml') -Value $workspace -Encoding UTF8

    Write-Step "Installing @deepseek-ai/dsh@$Version (a few minutes the first time)"
    $nodeDir = Split-Path -Parent $NodeExe
    $env:PATH = "$nodeDir;$env:PATH"
    Push-Location $Destination
    try {
        # Every install script here is either a native rebuild (the prebuilt
        # binaries already ship) or a chmod for a platform this package cannot
        # run on. Skipping them removes a hard dependency on a C++ toolchain.
        & $NodeExe $pnpmEntry install "@deepseek-ai/dsh@$Version" --prod --ignore-scripts --config.frozen-lockfile=false 2>&1 |
            Where-Object { $_ -notmatch '^\s*$' } |
            Select-Object -Last 8 |
            ForEach-Object { Write-Note $_ }
        if ($LASTEXITCODE -ne 0) { throw "pnpm install failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }

    foreach ($junk in 'node_modules/.bin') {
        Remove-Item (Join-Path $Destination $junk) -Recurse -Force -ErrorAction SilentlyContinue
    }
    # package.json, pnpm-workspace.yaml and the lockfile stay on purpose: the
    # launcher's "update dsh" runs `pnpm add` in this folder, and it needs the
    # hoisted linker setting to keep the tree copyable.
}

function Remove-PortableDeadWeight([string]$AppRoot) {
    # Packages reachable only through features a single-window portable install
    # does not expose, plus native builds for other platforms. dsh treats every
    # one of them as optional and reports the missing engine at use time.
    $modules = Join-Path $AppRoot 'node_modules'
    $targets = @(
        # Only payload that cannot run on this platform goes: the Windows x64 engines
        # and models stay, the WASM builds and the macOS/Linux/ARM/ia32 ones do not.
        '@deepseek-ai/libreoffice-kit-wasm',
        '@deepseek-ai/libreoffice-kit-darwin-arm64',
        '@deepseek-ai/libreoffice-kit-darwin-x64',
        '@deepseek-ai/libreoffice-kit-win32-arm64',
        'sherpa-onnx-win-ia32',
        'sherpa-onnx-darwin-arm64',
        'sherpa-onnx-darwin-x64',
        'sherpa-onnx-linux-arm64',
        'sherpa-onnx-linux-x64',
        # Native builds for platforms this package can never run on.
        'node-pty/prebuilds/darwin-arm64',
        'node-pty/prebuilds/darwin-x64',
        'node-pty/prebuilds/linux-arm64',
        'node-pty/prebuilds/linux-x64',
        'node-pty/prebuilds/win32-arm64',
        '@vscode/ripgrep-darwin-arm64',
        '@vscode/ripgrep-darwin-x64',
        '@vscode/ripgrep-linux-arm',
        '@vscode/ripgrep-linux-arm64',
        '@vscode/ripgrep-linux-ia32',
        '@vscode/ripgrep-linux-ppc64',
        '@vscode/ripgrep-linux-riscv64',
        '@vscode/ripgrep-linux-s390x',
        '@vscode/ripgrep-linux-x64',
        '@vscode/ripgrep-win32-arm64',
        '@vscode/ripgrep-win32-ia32',
        '@deepseek-ai/node-addon-system-darwin-arm64',
        '@deepseek-ai/node-addon-system-darwin-x64',
        '@deepseek-ai/node-addon-system-linux-arm64',
        '@deepseek-ai/node-addon-system-linux-x64',
        'node-addon-require-builtin-darwin-arm64',
        'node-addon-require-builtin-darwin-x64',
        'node-addon-require-builtin-linux-arm64-gnu',
        'node-addon-require-builtin-linux-arm64-musl',
        'node-addon-require-builtin-linux-x64-gnu',
        'node-addon-require-builtin-linux-x64-musl',
        'node-addon-require-builtin-win32-arm64-msvc',
        'node-addon-require-builtin-win32-ia32-msvc',
        '@koromix/koffi-darwin-arm64',
        '@koromix/koffi-darwin-x64',
        '@koromix/koffi-linux-arm64',
        '@koromix/koffi-linux-ia32',
        '@koromix/koffi-linux-loong64',
        '@koromix/koffi-linux-riscv64',
        '@koromix/koffi-linux-x64',
        '@koromix/koffi-openbsd-ia32',
        '@koromix/koffi-openbsd-x64',
        '@koromix/koffi-freebsd-arm64',
        '@koromix/koffi-freebsd-ia32',
        '@koromix/koffi-freebsd-x64',
        '@koromix/koffi-win32-arm64',
        '@koromix/koffi-win32-ia32',
        '@img/sharp-darwin-arm64', '@img/sharp-darwin-x64', '@img/sharp-linux-arm',
        '@img/sharp-linux-arm64', '@img/sharp-linux-ppc64', '@img/sharp-linux-riscv64',
        '@img/sharp-linux-s390x', '@img/sharp-linux-x64', '@img/sharp-linuxmusl-arm64',
        '@img/sharp-linuxmusl-x64', '@img/sharp-win32-arm64', '@img/sharp-win32-ia32',
        '@img/sharp-freebsd-wasm32', '@img/sharp-wasm32', '@img/sharp-webcontainers-wasm32'
    )

    $saved = 0
    foreach ($relative in $targets) {
        $path = Join-Path $modules $relative
        if (-not (Test-Path $path)) { continue }
        $size = (Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $path)) { $saved += $size }
    }


    # Source maps and debug symbols: never read during normal use, ~120MB together.
    Get-ChildItem $modules -Recurse -File -Include *.map,*.pdb -ErrorAction SilentlyContinue |
        ForEach-Object { $saved += $_.Length; Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }

    # Record the payload policy next to the tree so the launcher's in-place
    # update prunes an updated tree exactly like a fresh build.
    $record = @('# Removed by build.ps1 -Remove-PortableDeadWeight; the launcher''s') +
              @('# "update dsh" re-applies this list after a pnpm update.') + $targets
    Set-Content -Path (Join-Path $AppRoot '.dsh-desktop-prune.txt') -Value ($record -join "`n") -Encoding UTF8

    Write-Ok ("removed {0:N0} MB of payload this platform cannot run" -f ($saved / 1MB))
}

function Stage-Runtime([string]$RuntimeDir, [string]$NodeExe, [string]$PnpmEntry) {
    New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null
    Copy-Item $NodeExe (Join-Path $RuntimeDir 'node.exe') -Force

    # `dsh plugin` forwards to pnpm, so a portable install carries its own copy
    # plus a shim on PATH; without it the Plugins page cannot install anything.
    $pnpmPackage = Split-Path (Split-Path $PnpmEntry -Parent) -Parent
    $pnpmTarget = Join-Path $RuntimeDir 'pnpm'
    Remove-Item $pnpmTarget -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $pnpmTarget | Out-Null
    Copy-Item (Join-Path $pnpmPackage '*') $pnpmTarget -Recurse -Force
    $shim = @(
        '@echo off',
        'rem Run the portable pnpm with the portable node beside this file.',
        '"%~dp0node.exe" "%~dp0pnpm\bin\pnpm.cjs" %*'
    ) -join "`r`n"
    Set-Content -Path (Join-Path $RuntimeDir 'pnpm.cmd') -Value $shim -Encoding ASCII
}

function New-ThirdPartyNotices([string]$AppDir, [string]$DshVersion, [string]$Destination) {
    # dsh pulls in several hundred packages. Listing them with their declared
    # license is accurate and stays correct across upgrades; the full texts
    # travel inside each package folder in app/node_modules.
    $modules = Join-Path $AppDir 'node_modules'
    $rows = @()
    $packages = Get-ChildItem $modules -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin '.bin', '.pnpm' }
    foreach ($pkg in $packages) {
        $dirs = @($pkg)
        if ($pkg.Name.StartsWith('@')) {
            $dirs = @(Get-ChildItem $pkg.FullName -Directory -Force -ErrorAction SilentlyContinue)
        }
        foreach ($dir in $dirs) {
            $manifest = Join-Path $dir.FullName 'package.json'
            if (-not (Test-Path $manifest)) { continue }
            try {
                $json = Get-Content $manifest -Raw | ConvertFrom-Json
                $name = if ($json.name) { $json.name } else { $dir.Name }
                $license = if ($json.license) {
                    if ($json.license -is [string]) { $json.license } else { $json.license.type }
                } elseif ($json.licenses) { ($json.licenses | ForEach-Object { $_.type }) -join ', ' } else { 'see package' }
                $rows += [pscustomobject]@{ Name = $name; Version = $json.version; License = $license }
            }
            catch { }
        }
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('# Third-party notices')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('DSH Desktop bundles the following software. Each package keeps its own license;')
    [void]$sb.AppendLine('the full license text ships inside its folder under `app\node_modules`.')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Bundled by this repository')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| Component | Version | License |')
    [void]$sb.AppendLine('|---|---|---|')
    [void]$sb.AppendLine("| @deepseek-ai/dsh | $DshVersion | MIT |")
    [void]$sb.AppendLine('| Node.js | see runtime | MIT |')
    [void]$sb.AppendLine('| Microsoft Edge WebView2 Runtime (fixed version) | see webview2 | Microsoft proprietary, redistributable |')
    [void]$sb.AppendLine('| Microsoft.Web.WebView2 SDK | see exe folder | Microsoft proprietary, redistributable |')
    [void]$sb.AppendLine('| pnpm | see runtime\pnpm | MIT |')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## npm packages inside app\node_modules')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| Package | Version | Declared license |')
    [void]$sb.AppendLine('|---|---|---|')
    foreach ($row in ($rows | Sort-Object Name)) {
        [void]$sb.AppendLine("| $($row.Name) | $($row.Version) | $($row.License) |")
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine(('_Generated from the installed tree: {0} packages._' -f $rows.Count))
    Set-Content -Path $Destination -Value $sb.ToString() -Encoding UTF8
    Write-Note ("third-party notices: {0} packages" -f $rows.Count)
}

Write-Step 'Locating the C# compiler'
$csc = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '.NET Framework 4.x csc.exe not found; install it or run on a stock Windows 10/11.' }
Write-Note $csc

New-Item -ItemType Directory -Force -Path $CacheDir, $WorkDir | Out-Null

$webviewDir = Join-Path $OutDir 'webview2'
$appDir = Join-Path $OutDir 'app'
$runtimeDir = Join-Path $OutDir 'runtime'

$reuseWebView = if ($WithWebView2) { (-not $Force) -and (Test-WebView2 $webviewDir) } else { $true }
$reuseSdk = (-not $Force) -and (Test-Sdk $OutDir)
$reuseApp = (-not $Force) -and (Test-App $appDir $DshVersion)
$reuseRuntime = (-not $Force) -and (Test-Path (Join-Path $runtimeDir 'node.exe')) -and (Test-Path (Join-Path $runtimeDir 'pnpm.cmd'))
$nodeExe = Find-Node $CacheDir $NodeVersion
Write-Note "node: $nodeExe"

if ($reuseWebView -and $reuseSdk -and $reuseApp -and $reuseRuntime) {
    Write-Step 'Reusing the existing runtime, app and SDK'
    Write-Note "output: $OutDir"
    Write-Note 'pass -Force to rebuild them from scratch'
}
else {
    New-Item -ItemType Directory -Force -Path $OutDir, $runtimeDir | Out-Null

    if ($WithWebView2) {
        if (-not $reuseWebView) {
            Write-Step "Fetching the fixed-version WebView2 runtime ($WebView2Version)"
            $shellPkg = Join-Path $CacheDir "webview2.runtime.x64.$WebView2Version.nupkg"
            $corePkg = Join-Path $CacheDir "webview2.runtime.x64.core.$WebView2Version.nupkg"
            Get-Cached "https://api.nuget.org/v3-flatcontainer/webview2.runtime.x64/$WebView2Version/webview2.runtime.x64.$WebView2Version.nupkg" $shellPkg "webview2 runtime"
            Get-Cached "https://api.nuget.org/v3-flatcontainer/webview2.runtime.x64.core/$WebView2Version/webview2.runtime.x64.core.$WebView2Version.nupkg" $corePkg "webview2 core"

            Write-Step 'Assembling the WebView2 runtime folder'
            Expand-NuGet $shellPkg (Join-Path $WorkDir 'wv2shell')
            Expand-NuGet $corePkg (Join-Path $WorkDir 'wv2core')
            $shellFiles = Join-Path $WorkDir 'wv2shell/contentFiles/any/any/WebView2'
            $coreFiles = Join-Path $WorkDir 'wv2core/contentFiles/any/any/WebView2'
            if (-not (Test-Path $shellFiles)) { throw "unexpected package layout: $shellFiles" }
            if (-not (Test-Path $coreFiles)) { throw "unexpected package layout: $coreFiles" }
            Remove-Item $webviewDir -Recurse -Force -ErrorAction SilentlyContinue
            New-Item -ItemType Directory -Force -Path $webviewDir | Out-Null
            Copy-Item (Join-Path $shellFiles '*') $webviewDir -Recurse -Force
            Copy-Item (Join-Path $coreFiles 'msedge.dll') $webviewDir -Force
            if (-not (Test-WebView2 $webviewDir)) { throw "WebView2 runtime incomplete in $webviewDir" }
        }
        else { Write-Note 'webview2 runtime: reused' }
    }
    else {
        # The default package carries no engine: the launcher uses the runtime
        # already installed on the machine, and downloads a fixed version into
        # this folder only when there is none.
        if (Test-Path $webviewDir) { Remove-Item $webviewDir -Recurse -Force -ErrorAction SilentlyContinue }
        Write-Note 'webview2 runtime: not bundled (system runtime or first-run download)'
    }

    if (-not $reuseSdk) {
        Write-Step "Fetching the WebView2 SDK ($SdkVersion)"
        $sdkPkg = Join-Path $CacheDir "microsoft.web.webview2.$SdkVersion.nupkg"
        Get-Cached "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$SdkVersion/microsoft.web.webview2.$SdkVersion.nupkg" $sdkPkg "webview2 sdk"
        Expand-NuGet $sdkPkg (Join-Path $WorkDir 'sdk')
        Copy-Item (Join-Path $WorkDir 'sdk/lib/net462/Microsoft.Web.WebView2.Core.dll') $OutDir -Force
        Copy-Item (Join-Path $WorkDir 'sdk/lib/net462/Microsoft.Web.WebView2.WinForms.dll') $OutDir -Force
        Copy-Item (Join-Path $WorkDir 'sdk/runtimes/win-x64/native/WebView2Loader.dll') $OutDir -Force
    }
    else { Write-Note 'webview2 sdk: reused' }

    Stage-Runtime $runtimeDir $nodeExe (Install-Pnpm $nodeExe '11.28.5')

    if ($Variant -eq 'slim') {
        # The launcher installs dsh itself on first launch, so app\ ships empty -- even
        # if a previous run of the folder left a tree behind.
        $leftover = @(Get-ChildItem $appDir -Force -ErrorAction SilentlyContinue)
        if ($leftover.Count -gt 0) {
            Remove-Item $appDir -Recurse -Force
            Write-Note 'app: cleared the tree a previous run left behind'
        }
        New-Item -ItemType Directory -Force -Path $appDir | Out-Null
        Write-Note "app: empty, dsh is installed on first launch"
    }
    else {
        if (-not $reuseApp) {
            Install-App $appDir $DshVersion $nodeExe
            if ($Variant -ne 'full') { Remove-PortableDeadWeight $appDir }
            if (-not (Test-App $appDir $DshVersion)) { throw "installed dsh is not $DshVersion" }
        }
        else {
            # A reused tree may come from another variant; prune or leave it whole.
            if ($Variant -ne 'full') { Remove-PortableDeadWeight $appDir }
            Write-Note "app (dsh $DshVersion): reused"
        }
    }
}

Write-Step 'Compiling dsh-desktop.exe'
$icon = Join-Path $ScriptRoot 'src/app_dsh.ico'
$sources = @(Get-ChildItem -Path (Join-Path $ScriptRoot 'src') -Filter *.cs -File | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'no sources found under src' }
& $csc /nologo /target:winexe /platform:anycpu /optimize+ `
    "/out:$OutDir\dsh-desktop.exe" `
    "/win32icon:$icon" "/win32manifest:$ScriptRoot\src\app.manifest" `
    "/resource:$icon,dsh_default.ico" `
    "/resource:$ScriptRoot\src\app_black.ico,dsh_black.ico" `
    "/resource:$ScriptRoot\src\app_white.ico,dsh_white.ico" `
    "/r:$OutDir\Microsoft.Web.WebView2.Core.dll" `
    "/r:$OutDir\Microsoft.Web.WebView2.WinForms.dll" `
    /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    @sources
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

Write-Step 'Assembling the portable folder'
foreach ($file in 'README.md', 'README.en.md', 'LICENSE', 'dsh-desktop.ini') {
    $source = Join-Path $ScriptRoot $file
    if (Test-Path $source) { Copy-Item $source $OutDir -Force }
}
New-ThirdPartyNotices $appDir $DshVersion (Join-Path $OutDir 'THIRD_PARTY_NOTICES.md')
# Mirror the docs folder instead of merging into it, so a screenshot removed from
# the repository does not linger in the package.
$docsOut = Join-Path $OutDir 'docs'
if (Test-Path $docsOut) { Remove-Item $docsOut -Recurse -Force }
if (Test-Path (Join-Path $ScriptRoot 'docs')) {
    Copy-Item (Join-Path $ScriptRoot 'docs') $docsOut -Recurse -Force
}

# User data folders: created empty so the layout is obvious before first run.
foreach ($folder in 'workspace', 'data', 'profile') {
    $path = Join-Path $OutDir $folder
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        if ($folder -eq 'workspace') {
            $readme = @(
                'This folder is the agent workspace: the working directory every',
                'DeepSeek Harness session starts in. Put the projects you want the',
                'agent to work on here, or point workspace= in dsh-desktop.ini elsewhere.',
                '',
                '这个文件夹就是智能体的工作区，会话默认在这里开始。',
                '把要处理的项目放进来，或者改 dsh-desktop.ini 里的 workspace= 指向别处。'
            ) -join "`r`n"
            Set-Content -Path (Join-Path $path 'README.txt') -Value $readme -Encoding UTF8
        }
    }
}

# A folder that was run once carries logs, browser data and sessions; none of that
# ships, so the user folders are reset to empty.
foreach ($junk in 'dsh-desktop.log', 'dsh-desktop.state', 'selftest-result.json') {
    Remove-Item (Join-Path $OutDir $junk) -Force -ErrorAction SilentlyContinue
}
foreach ($folder in 'workspace', 'data', 'profile') {
    $path = Join-Path $OutDir $folder
    if (Test-Path $path) { Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    if ($folder -eq 'workspace') {
        $readme = @(
            'This folder is the agent workspace: the working directory every',
            'DeepSeek Harness session starts in. Put the projects you want the',
            'agent to work on here, or point workspace= in dsh-desktop.ini elsewhere.',
            '',
            '这个文件夹就是智能体的工作区，会话默认在这里开始。',
            '把要处理的项目放进来，或者改 dsh-desktop.ini 里的 workspace= 指向别处。'
        ) -join "`r`n"
        Set-Content -Path (Join-Path $path 'README.txt') -Value $readme -Encoding UTF8
    }
}

$size = (Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Step ("Ready: {0} ({1:N0} MB)" -f $OutDir, $size)

if ($Zip) {
    $zipPath = Join-Path $ScriptRoot ("build/dsh-desktop{0}-win64.zip" -f $(if ($Variant -eq 'slim') { '-slim' } elseif ($Variant -eq 'full') { '-full' } else { '' }))
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Write-Step 'Zipping (this takes a while for 700MB)'
    & tar.exe -a -c -f $zipPath -C $OutDir . 2>$null
    Write-Step ("Zipped: {0} ({1:N0} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB))
}

Write-Host ''
Write-Host 'Next: copy the folder to the target machine and run dsh-desktop.exe.' -ForegroundColor Green
