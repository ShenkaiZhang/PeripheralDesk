# 文件清单

## 源码仓库

| 文件 | 用途 |
| --- | --- |
| `Program.cs` | 应用入口、托盘面板、刷新流程、音量交互及诊断清单。 |
| `Design.cs` | 电量卡片、设备图形和滑块绘制。 |
| `Audio.cs` | Windows 音频设备枚举、默认设备切换、音量和静音。 |
| `AudioSessions.cs` | Windows 应用音频会话枚举、分组、独立音量及静音控制。 |
| `AudioMonitor.cs` | 后台订阅 Windows 音频会话创建、音量与播放状态通知；事件实时同步及设备恢复。 |
| `Mixer.cs` | 第二页应用音量列表、异步刷新、滑块及静音交互。 |
| `Package.ps1` | 构建或使用已验证 EXE，生成版本化 Windows ZIP 及 SHA-256 清单。 |
| `Devices.cs` | 外设枚举、USB 分组去重和接口检查。 |
| `BluetoothStatus.cs` | Windows 蓝牙连接状态。 |
| `Protocol.cs` | Windows 电量属性和 HID++ 报文解析。 |
| `Logitech.cs` | 已适配的罗技外设连接及电量查询。 |
| `RazerMouse.cs` | 已适配的雷蛇鼠标电量和充电状态查询。 |
| `CompxMouse.cs` | 已适配的 CompX 外设电量和充电状态查询。 |
| `HyperX.cs` | 部分 HyperX 耳机的接收器电量查询。 |
| `Fa09.cs` | FA09 接收器接口和电量查询。 |
| `SelfTest.cs` | 自动测试、Windows 只读设备检查及自有无声会话的音量/通知验证。 |
| `Build.cmd` | 编译 Windows x64 程序。 |
| `Test.cmd` | 构建测试程序并跑协议和界面测试。 |
| `Start.cmd` | 手动启动，缺少 EXE 时先编译。 |
| `Draw-Icon.ps1` | 重新生成 ICO。 |
| `PeripheralDesk.ico` | 程序和托盘图标的构建资源。 |
| `Panel-preview.png` | 演示面板截图。 |
| `Mixer-preview.png` | 应用音量第二页截图，使用演示数据。 |
| `Tray-preview.png` | 演示图标截图。 |
| `README.md` | 项目说明、运行、构建和兼容边界。 |
| `README.txt` | 随 Windows 下载包分发的使用、升级、自启动与卸载说明。 |
| `VALIDATION.md` | v0.5.0-preview 自动检查、真实 Windows 验证与证据边界。 |
| `FILES.md` | 本清单。 |
| `RELEASE_NOTES.md` | 当前版本说明及历史版本摘要。 |
| `THIRD_PARTY_NOTICES.md` | 第三方参考及适用范围。 |
| `LICENSE` | 外设台本体的 MIT 许可证。 |
| `LICENSE-HaloBattery.txt` | HaloBattery 上游许可证，不是本项目许可证。 |
| `.gitignore` | 防止构建产物、日志和本机设置误提交。 |

## Windows 下载包

下载包包含以下六个文件，不包含源码、调试文件或本机数据：

| 文件 | 用途 |
| --- | --- |
| `PeripheralDesk.exe` | Windows x64 可运行程序，未签名。 |
| `README.txt` | 使用、升级、自启动与卸载说明。 |
| `RELEASE_NOTES.md` | 本版功能、兼容边界及历史摘要。 |
| `LICENSE.txt` | 项目本体 MIT 许可证，由源码仓库的 `LICENSE` 复制。 |
| `LICENSE-HaloBattery.txt` | HaloBattery 上游 MIT 声明。 |
| `THIRD_PARTY_NOTICES.md` | 第三方参考和适用范围。 |

Release 同时提供 `SHA256SUMS.txt`。GitHub 自动生成的 Source code ZIP/TAR.GZ 与发布标签对应，需要使用 `Build.cmd` 构建。
