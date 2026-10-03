# chilimusic

chilimusic 是一款面向 Windows 的音乐播放器，支持本地音乐与 Navidrome 私人曲库。主界面用于浏览专辑和管理队列，任务栏控件与迷你播放器让你在工作时也能随手切歌。

[下载 Windows 版](https://github.com/sawanolin/chilimusic/releases/latest) · [更新记录](CHANGELOG.md) · [反馈问题](https://github.com/sawanolin/chilimusic/issues)

## 功能

- **任务栏播放控件**：嵌入主任务栏，显示专辑封面、歌名和歌手，支持上一首、播放 / 暂停、下一首与播放模式切换。
- **Navidrome 曲库**：浏览最近添加、专辑、歌手、歌单与最近播放，搜索歌曲、专辑和歌手，收藏喜欢的歌曲。
- **本地音乐**：打开或拖入音乐文件即可播放，无需连接服务器；支持 MP3、FLAC、WAV、APE、AAC、M4A、Ogg、Opus 等格式。
- **播放队列**：添加歌曲、安排下一首、拖动排序和移除歌曲，保存队列与播放位置。
- **四种播放模式**：顺序播放、列表循环、单曲循环和随机播放，也可直接随机播放全部歌曲或收藏。
- **桌面与系统控制**：迷你播放器、系统托盘菜单、Windows 媒体控制，以及 F7 播放、F8 暂停。
- **音频输出**：通过 libmpv 播放音频，使用系统默认输出设备，支持 WASAPI 独占模式；音频信息窗口可查看编码、采样率、位深等信息。
- **主题与偏好**：浅色、深色和跟随系统主题，支持开机启动、启动后进入托盘、关闭窗口后继续播放及切歌通知。

## 界面预览

### 主界面

浏览 Navidrome 专辑封面，同时查看播放队列和当前歌曲。

![Navidrome 专辑与正在播放的主界面](docs/images/main-server.png)

### 任务栏控件

收起主窗口后，仍可在任务栏上查看封面、控制播放和切换播放模式。

![带专辑封面的任务栏播放控件](docs/images/taskbar-playing.png)

## 下载与运行

运行环境：Windows 10 / 11（x64），[.NET 8 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/8.0)。

1. 安装 .NET 8 Desktop Runtime，选择 **Windows · x64**。
2. 从 [Releases](https://github.com/sawanolin/chilimusic/releases/latest) 下载 `chilimusic-1.0.0-win-x64.zip`。
3. 解压到任意文件夹，运行 `chilimusic.exe`。保留解压后的同目录文件。

下载页面同时提供 `SHA256SUMS.txt`，可用于校验压缩包。

## 开始听音乐

### 播放本地文件

点击主界面的“打开文件”选择一首或多首歌曲，也可以直接把音乐文件拖入主界面或迷你播放器。导入的文件会出现在“本地音乐”中。

本地封面可使用音乐文件同目录下的 `cover.jpg`、`folder.jpg`、`cover.png` 或 `folder.png`。

### 连接 Navidrome

1. 打开“设置”，填写服务器地址、用户名和密码。
2. 点击“测试连接”，确认连接成功后保存。
3. 从左侧进入专辑、歌手或歌单，双击专辑查看歌曲，双击歌曲开始播放。

顶部搜索栏支持搜索歌曲、专辑和歌手；在“本地音乐”页面搜索时，会搜索已导入的本地文件。

### 管理播放队列

在歌曲列表中右键，可选择“立即播放”“下一首播放”或“添加到队列”。右侧队列支持双击播放、拖动排序，以及右键移除、上移和下移。点击“清空”会停止播放并清空队列。

## 常用操作

| 操作 | 功能 |
| --- | --- |
| 任务栏上一首 / 播放 / 下一首按钮 | 控制当前播放 |
| 点击任务栏播放模式按钮 | 切换顺序播放、列表循环、单曲循环和随机播放 |
| 单击任务栏歌名或封面 | 打开迷你播放器 |
| 双击任务栏歌名或封面 | 打开主界面 |
| 在任务栏控件上滚动鼠标滚轮 | 调整音量 |
| 单击托盘图标 | 显示或收起迷你播放器 |
| 双击托盘图标 | 打开主界面 |
| 右键托盘图标或任务栏控件 | 打开播放、搜索、设置与退出菜单 |
| F7 / F8 | 播放 / 暂停，播放器运行期间生效 |

退出播放器后，F7 和 F8 恢复键盘原有功能。任务栏控件显示在主任务栏内，Windows 资源管理器重启后会自动重新连接。

## 常见问题

**没有 Navidrome 服务器可以使用吗？**

可以。直接打开本地音乐文件即可，服务器设置可以留空。

**启动时提示需要安装 .NET？**

安装 Windows 版 .NET 8 **Desktop Runtime x64**，安装完成后重新启动播放器。

**如何关闭窗口后继续听歌？**

在设置中启用“关闭窗口后继续播放”。关闭主窗口后，可通过任务栏或托盘继续控制；需要完全退出时，在右键菜单中选择“退出”。

**设置和曲库记录保存在哪里？**

保存在 `%APPDATA%\chilimusic`。登录密码使用 Windows DPAPI 加密，绑定当前 Windows 用户。

## 从源码构建

开发环境：Windows x64、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)、[7-Zip](https://www.7-zip.org/) 和 Git。项目使用 C#、WPF 与 libmpv，也可通过 `src/chilimusic.sln` 打开解决方案。

```powershell
git clone https://github.com/sawanolin/chilimusic.git
cd chilimusic
.\tools\setup-native.ps1
.\tools\build.ps1 -OutputDirectory .\dist
```

`setup-native.ps1` 下载固定版本的 libmpv 并校验 SHA256，`build.ps1` 生成 Windows x64 发行文件。完成后运行 `dist\chilimusic.exe`。

发行版使用 MiSans 字体。需要以相同字体构建时，从[小米官方字体页面](https://hyperos.mi.com/font/en/download/)取得 MiSans Regular、Medium、Semibold 静态字体，放入 `src/Resources/Fonts`；未提供这些字体时，应用使用系统字体。

## 开源许可与致谢

chilimusic 以 [GPL-3.0-or-later](LICENSE.txt) 协议开源。

- [mpv / libmpv](https://github.com/mpv-player/mpv)：音频播放引擎，许可与构建来源见 [libmpv 说明](licenses/mpv-NOTICE.txt)。
- [MiSans](https://hyperos.mi.com/font/en/download/)：界面字体，遵循[字体许可说明](licenses/MiSans-NOTICE.txt)。
- [Navidrome](https://www.navidrome.org/)：私人音乐服务器。

欢迎通过 [Issues](https://github.com/sawanolin/chilimusic/issues) 提交问题或建议。报告问题时请说明 Windows 版本、播放器版本、使用场景和复现步骤。
