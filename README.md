# 班级管理小师

面向班级课堂的 Windows 桌面工具：上课闹钟、全屏提醒、希沃自动登录、定时网页。C# + WPF（.NET 8，self-contained 单文件发布），后台托盘常驻，非上课时间全程静默。

## 功能

- 上课闹钟：按课程表提前 N 分钟全屏提醒并响铃。
- 希沃自动登录：响铃同时自动检测并登录当堂科目账号；遇到连堂保持希沃现状不做任何操作。
- 托盘控制：左键点击托盘图标即可开关闹钟、开关登录或退出全部。
- 定时网页（Smart URL）：到开始时间用系统默认浏览器（新窗口）打开指定网页，到结束时间自动关闭。
- 已关闭自动更新：程序不会联网检查或下载新版本。

## 目录结构

```
src/
  ClassManager.sln
  ClassManager.App/           WPF 主程序（托盘、全屏遮罩、设置界面、调度）
  ClassManager.Core/          配置模型 / JSON 存取 / DPAPI / 自启动 / 课程调度
  ClassManager.Seewo/         希沃（Seewo）UI Automation 自动登录
assets/                       icon.ico、ringtone.wav（内置兜底铃声）
installer/setup.iss           Inno Setup 6 打包脚本
docs/sdk.md                   课程表 JSON 格式 SDK
docs/使用说明.md              应用使用说明
class-site/                   发布到 GitHub Pages 的 /class/ 静态资源
build/                        构建脚本与中间产物
release/                      最终安装包
```

## 构建

环境要求：

- Windows 10/11 x64
- .NET 8 SDK（`winget install Microsoft.DotNet.SDK.8`）
- Inno Setup 6（`winget install JRSoftware.InnoSetup`）

一键构建：

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

产物：

- `build\publish\ClassManager.exe`：self-contained 单文件（约 66 MB）
- `release\ClassManagerSetup.exe`：安装包（可自定义安装路径、桌面/开始菜单快捷方式、开机自启）

手动构建（等价步骤）：

```powershell
dotnet publish src\ClassManager.App\ClassManager.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o build\publish
Copy-Item assets\icon.ico,assets\ringtone.wav build\publish\assets\ -Force
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\setup.iss
```

## 关键实现

- 全屏遮罩：`AlarmWindow` 使用 `SetWindowPos` 按物理像素铺满显示器（含任务栏区域），置顶、屏蔽 Alt+F4，点击后 3 秒倒计时关闭；紧急关闭热键 `Ctrl+Alt+Shift+Q`。副屏由 `ScreenCoverWindow` 补黑。
- 调度：`AlarmScheduler` 每秒根据「周几 → 课程表」映射计算触发点，提前 N 分钟响铃；同一分钟只触发一次。
- 铃声：WPF `MediaPlayer` 播放自定义 mp3/wav，缺失时使用内置 `assets/ringtone.wav`，响铃 15 秒；可选 SAPI 语音播报“上课时间到了”。
- 希沃登录：`SeewoLoginService` 通过 UI Automation 定位窗口/输入框/按钮，关键操作使用图标模板匹配（不依赖绝对像素），先检测并退出上一账号再登录；全流程超时兜底、失败静默，仅写日志（`%APPDATA%\ClassManager\logs\seewo.log`）。
- 定时网页：`SmartUrlService` 每秒检查开始/结束时间，到点用默认浏览器新窗口打开、结束时关闭该窗口；支持单次、每天、星期X至星期X（含跨周）重复。
- 凭据：Windows DPAPI（`CryptProtectData`）加密后以 `dpapi:` 前缀写入 `config.json`；导出 JSON 默认不含密码。
- 高 DPI：`app.manifest` 声明 PerMonitorV2，避免 225% 缩放下遮罩被系统拉伸。

## 数据位置

- 配置：`%APPDATA%\ClassManager\config.json`
- 状态：`%APPDATA%\ClassManager\state.json`
- 日志：`%APPDATA%\ClassManager\logs\`
- 首次启动会用默认浏览器打开 `https://newcyr.github.io/class/kj.html`

## 发布到 GitHub Pages（/class/）

1. 复制 `config.local.json.example` 为 `config.local.json`，填入 GitHub token（该文件已被 `.gitignore` 忽略）。
2. 执行：

```powershell
powershell -ExecutionPolicy Bypass -File tools\deploy-class.ps1
```

脚本会把 `class-site/` 下的文件上传到仓库 `newcyr.github.io` 的 `class/` 目录（默认分支 `main`），包括 `kj.html`、`sdk.md`、使用说明与 `downloads/ClassManagerSetup.exe`。

> 若本机无法访问 github.com，可先完成全部本地构建与验证，待网络可用后再执行部署脚本；下载区同时提供 gh-proxy 加速、镜像与原始链接。

## 验收自测

1. 双击 `release\ClassManagerSetup.exe` 安装（可自定义路径），桌面出现快捷方式。
2. 重启后托盘出现图标，无窗口弹出。
3. 设置一条“提前 1 分钟”的课程，到点全屏显示“上课时间到了，请快速回到位置，做好上课准备，这节课是：XX”，铃声约 15 秒，点击后 3 秒关闭。
4. 周日与周一至周六分别映射不同课程表，提醒按映射触发。
5. 手动填写与导入 JSON（示例见 `docs/sdk.md`）结果一致。
6. 配置希沃账号后，闹钟触发时自动检测登录状态并登录（失败静默，日志可查）。
7. 在“定时网页”中新建一条 1 分钟后的记录，到点浏览器打开、结束时自动关闭。

## 许可证

本项目依据 MIT 许可证授权，详见 [LICENSE](LICENSE)。

版权所有 (C) 2026 陈运然。保留所有权利。
Copyright (c) 2026 Chen Yun ran. All rights reserved.

E-mail: cyrcyrgo@outlook.com 或 yjs_cla@126.com
