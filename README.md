<p align="center">
  <img src="assets/branding/logo-full.png" width="360" alt="AstraTaleStudio 工作室 Logo">
</p>

<p align="center"><strong>本项目为 AstraTaleStudio 作品</strong></p>

# 星语互联

让 Android 手机与 Windows 电脑通过 USB 或 Wi-Fi 协作。图形化选设备、投屏和操控，也能传文件、看相册，并让手机播放电脑声音。

[下载 Windows 版](https://github.com/yangyv1016/XingyuConnect/releases/latest) · [连接与使用说明](使用说明.md) · [开发说明](docs/DEVELOPMENT.md)

## 开始使用

1. 从 Releases 下载 Windows x64 压缩包，完整解压。
2. 双击 `XingyuConnect.exe`。无需安装 .NET，不需要打开终端或输入命令。
3. 手机开启 USB 调试并授权电脑，或从“局域网连接”按向导连接。
4. 选择设备，打开操作窗口。

请保留整个解压文件夹，旁边的组件文件夹是程序运行所需。支持 Windows 10/11 x64；实际功能兼容性取决于手机系统和应用。

## 可以做什么

- 独立操控窗口、等比缩放、全屏，以及返回、主页、多任务等操作键。
- 手机显示屏息屏后继续投屏；使用系统电源键可能锁屏。
- 双向文件传输、相册浏览、应用搜索与收藏。
- 手机声音在手机、电脑或双端播放；电脑传音独立开关。
- 通过 VB-CABLE 将“手机音频”作为 Windows 播放设备使用。
- 电脑输入法直输与应用独立窗口仍在完善；不兼容应用可回退普通投屏。

设备列表在连接期间仍可选择，选择条目不会立即中断当前会话。“断开连接”停止投屏和电脑传音；单独关闭投屏窗口则保留传音。

## 遇到问题

先查看 [连接须知](使用说明.md)。运行日志在本机 `%LOCALAPPDATA%\XingyuConnect\Logs`，可从“使用说明 → 打开日志文件夹”进入。每次启动生成一个带时间和进程编号的文件，关闭程序后保留。日志不会自动上传；分享前可检查其中的设备地址、名称和文件路径。

已验证的范围及待验证项目见 [验证说明](docs/VALIDATION.md)。当前版本为早期版本，完整 Windows 拼音候选选词与部分厂商兼容性仍待验证。

## 第三方组件

核心投屏基于 [scrcpy](https://github.com/Genymobile/scrcpy)，传音基于 [AudioShare](https://github.com/ysbing/AudioShare)，中文输入使用 [ADBKeyboard](https://github.com/senzhk/ADBKeyBoard)。许可与对应源码说明见 [第三方声明](第三方声明.md)。本项目不属于华为或小米官方产品。

VB-CABLE 由 [VB-Audio](https://vb-audio.com/Cable/) 提供，是捐赠软件，欢迎[支持作者或购买许可](https://vb-audio.com/Services/licensing.htm)。驱动保持原包，由官方图形安装器安装。
