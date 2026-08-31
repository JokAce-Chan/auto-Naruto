# 替身计时器（NaruttoTimer / auto-Naruto）

配合雷电模拟器游戏使用的"菱形识别 + 15 秒倒计时"工具。

## 功能
- scrcpy 视频流实时取帧（30fps），识别屏幕顶部左右两侧菱形区域
- 值恰好减 1 → 启动/重置 15.00s 倒计时；左右独立并行显示
- 回合切换（4↔6 格）自动判定；加载动画期间不误触发
- 半透明置顶框（透明度/大小/锁定可调）
- 取色校准、区域框选、识别数据保存

## 环境要求
- Windows 11
- .NET 8 SDK（开发）
- 雷电模拟器（2560×1440，ADB 开启，设备号 emulator-7834）
- scrcpy 4.1（后续放 vendor/scrcpy）

## 开发
```powershell
dotnet build
dotnet run --project src/NaruttoTimer.App
```

## 目录结构
- `src/` 源码（按 M1~M6 模块划分）
- `tests/` 单元测试
- `vendor/scrcpy/` scrcpy 组件（scrcpy.exe、adb.exe、server、FFmpeg DLL）
- `assets/` 图标与素材
- `design/` 主界面原型与设计文档
- `data/` 运行数据（不入库）

## 文档
- `PRD.md` 产品需求文档
- `DEVELOPMENT_PLAN.md` 开发计划
- `GITHUB_GUIDE.md` GitHub 从零使用指南

## 版本
v0.1（开发中）
