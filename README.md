# 机械臂全功能控制套件 (C# / WPF · Fluent Design)

基于 **.NET 10 + WPF + WPF-UI (Fluent Design)** 重构的机械臂全功能上位机控制套件，完整对应固件 **V4.12** 通信协议，在 Visual Studio 中开箱即用。

## 核心功能

- **三模通信传输**
  - **串口 (Serial)**：支持 `9600 ~ 115200 bps`，自动枚举可用端口并读取 Windows 设备友好名称。
  - **低功耗蓝牙 (BLE)**：基于 Windows 原生 WinRT GATT API 实时增量扫描与 RSSI 展示；内置 `JDY-29`、`HM-10`、`Nordic UART (NUS)`、`HC-08`、`TI CC2540` 透传预设，支持历史特性复用与手动 TX/RX 绑定窗口。
  - **经典蓝牙 SPP (RFCOMM)**：基于 Winsock2 (`AF_BTH / BTHPROTO_RFCOMM`) 直连通道 1~4，支持读取 Windows 已配对经典蓝牙设备与配对说明向导。
- **状态仪表盘**：6 项实时运行总览 KPI、16 路舵机角度矩阵（带固件焦点通道高亮与使能遮罩）、等宽姿态快照与一键复制。
- **手动姿态控制**：16 通道滑块、±1° 步进微调与数字直输；支持同步/非同步双模式、全部归中 (90°) 与全部归零 (0°)。
- **动作序列编排**：回放控制（播放/暂停/继续/停止）、`0.5x / 1.0x / 2.0x` 倍速切换、关键帧录制、任意步前插入姿态 (`rec_insert`)、停留延时 (`rec_delay 0~600s`) 与清空序列。
- **环境实时监控**：DHT11 温湿度可视化进度条与超限安全报警提示。
- **硬件与本地系统设置**：16 路舵机掩码开关与 `servo_save` 下发、温湿度报警阈值写入 (`set_thresh`)、`reset_arm` / `help` / `reset_system` 硬件调试，以及与原版兼容的 `arm_suite_config.json` 原子持久化。
- **可拖拽/收起调试终端**：支持上下拖拽调高、双击收起/展开、多行高亮选择复制、日志导出 (`.log` / `.txt`)、自动滚动开关与带上下键历史回溯的 `CMD >` 快捷指令栏。
- **明暗双主题**：一键切换 Windows 11 Mica 浅色 / 深色外观并持久化保存。

## 开发与构建

### 使用 Visual Studio
使用 **Visual Studio 2022 / 2026** 打开 `ArmControlSuite.sln`，将 `ArmControlSuite` 设为启动项目后按 `F5` 运行。

### 使用 .NET CLI
```powershell
# 构建与运行测试
dotnet build ArmControlSuite.sln
dotnet test ArmControlSuite.sln

# 发布独立单文件可执行程序 (输出至 publish/ 目录)
dotnet publish src\ArmControlSuite\ArmControlSuite.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish
```
