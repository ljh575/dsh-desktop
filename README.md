# DSH Desktop

> 把 [DeepSeek Harness](https://github.com/deepseek-ai) 的网页版装进一个文件夹：自带 Node 运行时，**dsh 在首次启动时自动下载**，双击 `dsh-desktop.exe` 即可使用。目标机不需要装 Node、不需要管理员权限，也不写注册表。

[English](README.en.md) | 中文 · 当前版本 **1.1.0**

## 这是什么

`dsh web` 要在本机跑一个 Node 服务，再用浏览器打开；内网机器往往既没有 Node，也装不了软件。

DSH Desktop 把三件事做在一个绿色目录里：

1. 自带 Node 运行时，**首次启动用 pnpm 安装 npm `latest` 标签的 `@deepseek-ai/dsh`**，之后每次都直接启动服务
2. 优先使用系统 WebView2，没有就自动下载一份到本目录（不装系统组件）
3. 一个自绘窗口读取服务打印的带 token 地址并直接导航，**第一次打开就是登录状态**

## 特性

- **小体积开箱即用**：解压 → 双击 → 首次自动下载 dsh → 进入会话界面，安装包约 40MB
- **沉浸式顶栏**：无系统标题栏，40px 自绘栏按页面真实底色与分界绘制；页面切明暗时**同步跟随**（事件驱动，无轮询）
- **界面贴合 DSH**：设置、关于、更新、菜单全部使用 DSH 的配色、圆角、行高与滚动条；文案跟随 DSH 的界面语言（`zh` `en` `ja` `es` `fr` `de` `ru` `pt`）
- **两种角色**：默认自带服务；`server=0` 时退化为纯客户端，只连 `url=`
- **dsh 独立于安装包**：首次启动下载、菜单里「检查 dsh 更新…」就地替换，失败自动回滚
- **真正便携**：配置、密钥、会话、插件都在本目录 `data\`，删掉文件夹即卸载
- **不留孤儿进程**：Job Object 绑定服务，主程序退出或崩溃时服务一起结束
- **单实例保护**：同一数据目录只允许一个实例，避免并发写会话历史
- **可排查**：`dsh-desktop.log` 记录启动、服务输出、更新与下载全过程

## 快速开始

```
1. 解压整个文件夹（例如 D:\DSH Desktop\）
2. 双击 dsh-desktop.exe
3. 设置 → 模型，填入 API 密钥（或用 DeepSeek 账号登录）
```

首次启动会先下载 npm `latest` 标签的 dsh（约 490MB，含 Office 转换引擎与语音模型），启动页有**进度条（带百分比）和已写入体积**，完成后自动继续初始化工作区与 profile；之后每次 1–2 秒。下载失败会在窗口和日志里说明原因。


- **只当客户端**：齿轮 → 关闭「启动内置 dsh web 服务」→ 填服务地址 → 保存并重启
- **换项目**：把项目放进 `workspace\`，或改设置里的工作区路径

## 目录结构

```
dsh-desktop.exe              启动器（WinForms + WebView2，源码在 src\）
dsh-desktop.ini              配置
runtime\                     Node 运行时
app\node_modules\            dsh 及其依赖（首次启动下载，默认不随包提供）
app\.dsh-desktop-prune.txt   安装后要裁掉的负载清单
webview2\                    固定版 WebView2 内核（默认不含，缺运行时才下载）
data\                        DSH_HOME：配置、密钥、会话、插件
workspace\                   智能体默认工作区
profile\                     浏览器数据（cookie、登录态）
dsh-desktop.log              运行日志
```

## WebView2 从哪来

默认包**不带**内核（会多 500MB）。启动时按顺序查找：

1. 程序目录下的 `webview2\`（之前下载过，或用 `-WithWebView2` 构建）
2. 本机已安装的 WebView2 运行时
3. 都没有 → 弹窗说明后从 NuGet 下载 `webview2version` 指定的版本并解压到 `webview2\`

下载只写本程序目录，不装系统组件；取消下载则退出。dsh 的下载（首次启动）走的是同一条思路，只是用 pnpm 从 npm registry 取。

想在已装 WebView2 的机器上验证这条路径：设环境变量 `DSH_DESKTOP_NO_WEBVIEW2=1`，启动器会当作本机没有运行时，完整走一遍「检测失败 → 下载 → 用下载副本启动」。

## 构建

仓库**只放源码**：dsh 与 WebView2 内核都不入库。默认包不带 dsh（首次启动下载），也不带 WebView2（优先用系统运行时）。

三种产物，按需要选：

| 变体 | 命令 | 产物 | 体积（目录 / 压缩包） | 说明 |
|---|---|---|---|---|
| **精简版**（默认） | `build.ps1 -Zip` | `dsh-desktop-slim-win64.zip` | 113 MB / **39 MB** | 不含 dsh，首次启动联网下载（约 490MB） |
| **with dsh 版** | `build.ps1 -Variant dsh -Zip` | `dsh-desktop-win64.zip` | 609 MB / **203 MB** | dsh 已内置（npm `latest`）并裁剪，开箱即用 |
| **full 版** | `build.ps1 -Variant full -Zip` | `dsh-desktop-full-win64.zip` | 1182 MB / **466 MB** | 内置 dsh（npm `latest`）+ **WebView2 内核**，且**完全不裁剪**；完全不联网也能跑 |

一次构建全部三种：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -All
```

其它参数：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Variant dsh -Force        # 从头重建某个变体（仍取 npm latest）
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Variant dsh -DshVersion 0.2.2-rc.3   # 指定内置版本
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Variant dsh -WithWebView2 # 给 with dsh 版也带上 WebView2 内核
```

每个变体输出到 `build\<变体名>\`；加 `-Zip` 在 `build\` 下生成对应压缩包。

脚本做的事：找本机 Node（≥24）或下载官方 24.x → 准备 pnpm → 从 NuGet 取 WebView2 SDK → 用系统自带 `csc.exe` 编译启动器 → 组装成可直接拷走的目录。默认**不安装 dsh**（约 40MB 的包），加 `-WithDsh` 才会把 dsh 打进 `app\`（约 400MB，适合完全离线）。

不需要 Visual Studio、.NET SDK 或 NuGet 客户端。增量构建只重编 exe（约 1 秒）；Node 与 SDK 的下载物留在 `build\.cache`，之后可离线重建。

### 裁剪

只删两类别的东西：**本机跑不了的二进制**，以及**正常使用永远读不到的调试文件**。

| 删除 | 体积 | 影响 |
|---|---|---|
| WASM 版引擎（`libreoffice-kit-wasm`、`sharp-wasm32` 等） | 约 145 MB | 无（本机用原生引擎） |
| 其它平台/架构的二进制（macOS、Linux、`win32-arm64`、`win32-ia32`） | 约 12 MB | 无（本包只在 Windows x64 运行） |
| `*.map`、`*.pdb` | 约 121 MB | 无（source map 只给浏览器 devtools 用，调试符号只在原生崩溃栈里用） |

**保留**：`libreoffice-kit-win32-x64`（Office 转换）、`sherpa-onnx-win-x64`（语音输入），以及全部 Windows x64 原生模块。合计约 490 MB。

需要连可选依赖一起保留时加 `-Full`（不做任何裁剪）。

## 配置

两种改法等价：齿轮（`Ctrl+,`）里的**设置菜单**，或直接编辑 `dsh-desktop.ini`。菜单写回同一份文件，注释不丢。

| 键 | 默认 | 说明 |
|---|---|---|
| `server` | `1` | `1` 启动内置 `dsh web`；`0` 只作客户端，必须填 `url` |
| `url` | `http://127.0.0.1:3080/` | `server=0` 时要连的地址，首次可带 `?token=` |
| `host` | `127.0.0.1` | 监听地址（`server=1`） |
| `port` | `0` | 监听端口；`0` 每次自动挑空闲端口 |
| `workspace` | `workspace` | 智能体工作区根目录 |
| `data` | `data` | DSH_HOME（配置/密钥/会话/插件） |
| `profile` | `profile` | 浏览器数据目录 |
| `runtime` | `runtime` | Node 运行时目录 |
| `app` | `app` | 内置 dsh 目录 |
| `args` | 空 | 追加给 `dsh web` 的参数 |
| `icon` | `default` | `default` 官方图标 / `auto` 跟随系统主题 / `light` 黑鲸 / `dark` 白鲸 |
| `closeaction` | `ask` | 点关闭按钮时：`ask` 每次询问 / `tray` 最小化到托盘 / `quit` 直接退出 |
| `telemetry` | `0` | 是否上报匿名使用统计 |
| `startupseconds` | `120` | 等服务的超时秒数 |

相对路径都相对 exe 所在目录；文件缺失会自动重建。

### 命令行

每个 ini 键都有等价参数（命令行优先于 ini），适合脚本、快捷方式或临时试参数：

| 参数 | 等价的 ini 键 | 说明 |
|---|---|---|
| `--server=0` / `--server=1` | `server` | 关掉或打开内置服务 |
| `--client` | `server=0` | 同上，写法更短的开关 |
| `--url=<地址>` | `url` | 客户端模式要连的地址 |
| `--host=<地址>` | `host` | 监听地址 |
| `--port=<端口>` | `port` | 监听端口，`0` 为自动 |
| `--workspace=<目录>` | `workspace` | 工作区根目录 |
| `--data=<目录>` | `data` | DSH_HOME |
| `--profile=<目录>` | `profile` | 浏览器数据目录 |
| `--runtime=<目录>` | `runtime` | Node 运行时目录 |
| `--app=<目录>` | `app` | 内置 dsh 目录 |
| `--args="<参数>"` | `args` | 追加给 `dsh web` |
| `--icon=<样式>` | `icon` | `default` 官方图标 / `auto` 跟随系统主题 / `light` 黑鲸 / `dark` 白鲸 |
| `--closeaction=ask` / `=tray` / `=quit` | `closeaction` | 关闭按钮行为；旧的 `--closetotray=1` 仍被识别为 `tray` |
| `--webview2version=<版本>` | `webview2version` | 临时指定要下载的 WebView2 版本 |
| `--telemetry=0` / `=1` | `telemetry` | 匿名统计 |
| `--startupseconds=<秒>` | `startupseconds` | 启动超时 |

工具型参数（不改配置）：

| 参数 | 作用 |
|---|---|
| `--selftest` | 跑一遍「启动服务 → 载入页面」，把结果写进 `selftest-result.json` 后退出 |
| `--update-dsh=<版本>` | 就地更新内置 dsh 到指定版本，完成后退出 |
| `--download-webview2` | 强制走「下载 WebView2」流程 |
| `--bare` | 不加载任何补丁覆盖层 |

每次启动会在日志里打印一行 `config: ...`，列出解析后的全部取值与展开路径，可用来核对参数是否生效。

## 界面

- **设置**（齿轮 / `Ctrl+,`）：启动（内置服务、地址、监听、端口、超时）、外观（图标、关闭按钮）、目录、高级（附加参数、统计）。改完点「保存并重启」立即生效
- **`⋯` 菜单**：设置、检查 dsh 更新、在浏览器中打开、复制地址、开发者工具、打开工作区/数据目录/配置文件、关于
- **托盘菜单**：显示窗口、设置、关于、退出（托盘图标始终创建，`closeaction=tray/ask` 收起窗口后从这里恢复）
- **关于**：启动器版本、dsh 版本、Node、WebView2、模式、地址、目录、日志位置、项目地址，可一键复制

## 更新 dsh

`⋯` →「检查 dsh 更新…」：

- **可选通道**：`latest`（发布版）或 `next`（预览版），切换后立刻重新查询该通道的版本
- **强制重装**：打开后允许安装同版本或更旧的版本（默认不允许降级）
- 点「立即更新」：先停掉内置服务，在 `data\update-staging` 用 pnpm 装新树，校验版本后替换 `app\node_modules`；替换失败会重试并**回滚**
- 换完按 `.dsh-desktop-prune.txt` 重新裁剪，体积与新构建一致
- 成功后询问是否重启

命令行等价形式：`dsh-desktop.exe --update-dsh=0.2.2-rc.3`（跑完退出）。

## 图标

| 值 | 预览 | 说明 |
|---|---|---|
| 官方图标 (`default`) | ![default](docs/ic-default.png) | DSH 官方图标（圆角底 + 鲸鱼），默认 |
| 跟随系统主题 (`auto`) | ![auto](docs/ic-auto.png) | 跟随系统：浅色用黑鲸、深色用白鲸 |
| 黑鲸 (`light`) | ![light](docs/ic-light.png) | 始终用黑鲸，适合浅色背景 |
| 白鲸 (`dark`) | ![dark](docs/ic-dark.png) | 始终用白鲸，适合深色背景 |

exe 的静态文件图标固定为官方图标；窗口/任务栏图标随设置变化。标题栏的鲸鱼另有一套规则：跟随页面明暗自动切换（深色页面白鲸、浅色页面黑鲸），不受这里的选择影响。

## 关于本项目

- 图标取自 DSH 官方资源，仅用于识别本工具
- 与 DeepSeek 官方无关
- 内置的 `@deepseek-ai/dsh` 及其依赖遵循各自许可证

## License

[MIT](LICENSE)
