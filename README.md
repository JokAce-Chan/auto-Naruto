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
- scrcpy 4.1（本地 `scrcpy-win64-v4.1/`，发布时拷入 `vendor/scrcpy`）

## 开发与测试
```powershell
# 构建
.\tools\dotnetw.ps1 build NaruttoTimer.sln
# 运行全部单元测试（自研轻量框架，返回码 0 为通过）
.\tools\dotnetw.ps1 run --project tests\NaruttoTimer.Tests
# 启动主程序
dotnet run --project src/NaruttoTimer.App
```

## 发布（P7）
```powershell
.\tools\publish.ps1                     # 默认：框架依赖，离线可用（本机需 .NET 8 运行时）
.\tools\publish.ps1 -SingleFile         # 框架依赖单文件（需联网）
.\tools\publish.ps1 -SelfContained      # 自包含单文件（无需安装 .NET，需联网）
```
发布产物在 `publish/`，含 `NaruttoTimer.App.exe` 与 `vendor/scrcpy/`。

## 目录结构
- `src/` 源码（按 M1~M6 模块划分）
- `tests/NaruttoTimer.Tests/` 单元测试
- `tools/` 构建/发布/图标脚本
- `vendor/scrcpy/` scrcpy 组件（发布时生成，不入库）
- `assets/` 图标与素材
- `design/` 主界面原型与设计文档
- `data/` 运行数据（不入库）

## 文档
- `PRD.md` 产品需求文档
- `DEVELOPMENT_PLAN.md` 开发计划
- `GITHUB_GUIDE.md` GitHub 从零使用指南
- `docs/使用说明.md` 最终用户使用说明
- `docs/识别优化会话记录.md` 识别优化基线（下一轮优化参考）

## 版本
v0.1（P1~P7 开发完成，待真机联调）