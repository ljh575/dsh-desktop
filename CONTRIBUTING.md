# 参与开发

## 环境

只需要 Windows：构建脚本用系统自带的 `csc.exe` 编译启动器，不需要 Visual Studio 或 .NET SDK；首次构建需要联网下载 Node 与 WebView2 SDK，之后可离线增量构建。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip          # 精简版
powershell -ExecutionPolicy Bypass -File .\build.ps1 -All          # 三种产物
.\build\slim\dsh-desktop.exe --selftest                            # 自检
```

## 代码结构

| 文件 | 负责 |
|---|---|
| `src/Program.cs` | 入口、配置解析（ini + 命令行）、`App` 环境、内置服务管理 |
| `src/MainForm.cs` | 无边框窗口、自绘顶栏、菜单、托盘、首次安装与主题同步 |
| `src/SettingsDialog.cs` | 设置面板（分区、行、控件布局） |
| `src/Dialogs.cs` | 关于 / 更新 / 确认 / 进度等对话框，原生 P/Invoke |
| `src/Theme.cs` | 调色板、圆角、自绘控件（按钮、开关、下拉、滚动条、菜单） |
| `src/Maintenance.cs` | dsh 安装与更新、裁剪、版本比较 |
| `src/WebViewRuntime.cs` | WebView2 运行时的查找与下载 |
| `src/Lang.cs`, `src/StringsExtra.cs` | 启动器自身的多语言文案 |

## 约定

- **只用系统自带工具链**：新增代码必须能被 .NET Framework 4.x 的 `csc.exe` 编译（C# 5 语法，不要用 `var` 之外的新特性、`async` 之外的新关键字）
- **配色不写死**：颜色从 `Theme` 取，`Theme` 从页面采样；不要在控件里硬编码 RGB
- **不引入 nuget 包**：仓库只依赖 `System.*` 与 WebView2 的官方 SDK DLL（由 `build.ps1` 取得）
- **改动后必须能通过** `dsh-desktop.exe --selftest`

## 提交前自查

1. `build.ps1 -Zip` 能干净构建
2. `--selftest` 返回 `{"ok":true}`
3. 没有把 `data/`、`profile/`、构建产物提交进来
4. 面向用户的文案走 `Strings.T(...)`，不要硬编码中文或英文

## 许可

提交的代码按 [MIT](LICENSE) 授权。
