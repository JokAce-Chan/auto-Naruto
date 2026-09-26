# 替身计时器（NaruttoTimer / auto-Naruto）

配合雷电模拟器游戏使用的“能量条识别 + 14.5 秒倒计时”工具。

## 功能
- scrcpy 视频流实时取帧，识别屏幕顶部左右两侧能量条（整帧 1280×720，不再裁切顶部条带）
- AI 识别模式（ONNX 空豆模型 `best.onnx`）：模型不可用时不再回退，`[开始识别]` 会明确提示
- 判定方式可选：**值判定**（值恰好减 1）/ **空豆判定**（空豆恰好加 1），工具栏下拉框随时切换
- 稳定 3 帧后按判定方式触发 → 启动/重置 14.50s 倒计时；左右两侧独立并行显示
- 半透明置顶框（背景透明度与数字透明度分开调节 / 尺寸 / 锁定）
- 区域标注（主界面预览直接拖拽 + 弹窗输入框精确微调）、识别数据保存
- 多设备识别与选择（下拉框白底黑字，识别运行中切换设备会提示先停止）

## 识别与规则
- 值语义：`值 = 6 − 空豆数`，clamp 到 0~6（值上限 6，使空豆 0~6 全程可见）
- AI 模式：`best.onnx`（YOLOv8，单类=空豆），输入 `[1,3,128,128]`，输出 `[1,5,336]`；
  左右能量条垂直拼接后一次推理，再按中心点归属左右
- 触发规则：`StableValueTracker(3)` + 值判定（值恰好减 1）/ 空豆判定（空豆恰好加 1）+ 14.5s 倒计时

## 环境要求
- Windows 11
- .NET 8 SDK（开发）
- 雷电模拟器（ADB 开启，设备号形如 emulator-7834）
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
发布产物在 `publish/`，含 `NaruttoTimer.App.exe`、`assets/`（识别模型）与 `vendor/scrcpy/`。

## 目录结构
- `src/` 源码（按 M1~M6 模块划分）
- `tests/NaruttoTimer.Tests/` 单元测试
- `tools/` 构建/发布/图标脚本
- `vendor/scrcpy/` scrcpy 组件（发布时生成，不入库）
- `assets/` 图标与素材
- `data/` 运行数据（不入库）

## 文档
- `docs/使用说明.md` 最终用户使用说明

## 版本
v0.3（移除传统模式；AI 模式内新增「值判定 / 空豆判定」可切换；值上限 6）
