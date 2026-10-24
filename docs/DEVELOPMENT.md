# 开发说明

需要 Windows x64 和 .NET 10 SDK。第三方运行组件固定在 `vendor`，保留上游许可与源码归档；不要用个人设备配置覆盖此目录。

```powershell
dotnet build .\src\XingyuConnect.csproj -c Release
dotnet run --project .\tests\Smoke\Smoke.csproj -c Release
.\scripts\Publish.ps1
```

发布脚本生成 `artifacts/XingyuConnect-<版本>-win-x64`、同名 ZIP 及 SHA-256 文件。用户包只提供图形主程序，不附终端启动脚本、调试符号、历史验证报告或开发说明。许可证要求的第三方源码归档和可替换组件仍随包保留。

目录说明：`src` 为 WinForms 客户端，`assets/branding` 为嵌入图标，`native/audioshare` 为 AudioShare 扩展源码与构建脚本，`tests/Smoke` 为不显示窗口的检查，`docs` 为维护文档。

本机设置与日志保存在 `%LOCALAPPDATA%/XingyuConnect`，不提交仓库、不打包、不自动上传。日志只记录应用生命周期、连接、传输结果和错误，不主动记录输入正文、剪贴板正文或配对码。内部驱动初始化参数只供图形界面提权使用，不是面向用户的命令行接口。

原生音频扩展用 Zig 0.13.0 构建：

```powershell
.\native\audioshare\build.ps1 -ZigPath C:\Tools\zig\zig.exe
```

主项目尚未声明额外开源授权；第三方组件分别遵循各自许可，品牌素材权利不因公开仓库而改变。
