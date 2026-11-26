# 文件清单

## 源码仓库

| 文件 | 用途 |
| --- | --- |
| `Program.cs` | 应用入口、托盘面板、刷新流程、音量交互及诊断清单。 |
| `Design.cs` | 电量卡片、设备图形和滑块绘制。 |
| `Audio.cs` | Windows 音频设备枚举、默认设备切换、音量和静音。 |
| `Devices.cs` | 外设枚举、USB 分组去重和接口检查。 |
| `BluetoothStatus.cs` | Windows 蓝牙连接状态。 |
| `Protocol.cs` | Windows 电量属性和 HID++ 报文解析。 |
| `Logitech.cs` | 已适配的罗技外设连接及电量查询。 |
| `RazerMouse.cs` | 已适配的雷蛇鼠标电量和充电状态查询。 |
| `CompxMouse.cs` | 已适配的 CompX 外设电量和充电状态查询。 |
| `HyperX.cs` | 部分 HyperX 耳机的接收器电量查询。 |
| `Fa09.cs` | FA09 接收器接口和电量查询。 |
| `SelfTest.cs` | 自动测试和 Windows 只读验证入口。 |
| `Build.cmd` | 编译 Windows x64 程序。 |
| `Test.cmd` | 构建测试程序并跑协议和界面测试。 |
| `Start.cmd` | 手动启动，缺少 EXE 时先编译。 |
| `Draw-Icon.ps1` | 重新生成 ICO。 |
| `PeripheralDesk.ico` | 程序和托盘图标的构建资源。 |
| `Panel-preview.png` | 演示面板截图。 |
| `Tray-preview.png` | 演示图标截图。 |
| `README.md` | 项目说明、运行、构建和兼容边界。 |
| `FILES.md` | 本清单。 |
| `RELEASE_NOTES.md` | v0.4.2 预览版发布说明。 |
| `THIRD_PARTY_NOTICES.md` | 第三方参考及适用范围。 |
| `LICENSE-HaloBattery.txt` | HaloBattery 上游许可证，不是本项目许可证。 |
| `.gitignore` | 防止构建产物、日志和本机设置误提交。 |

## Windows 下载包

下载包包含 `PeripheralDesk.exe`（可直接运行的程序）、`README.txt`（运行与卸载）、`THIRD_PARTY_NOTICES.md` 和 `LICENSE-HaloBattery.txt`。不包含源码和调试文件。
