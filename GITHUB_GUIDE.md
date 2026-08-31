# GitHub 从零开始使用指南（Windows + PowerShell）

> 适用：本项目（替身计时器 NaruttoTimer）从零创建仓库、上传代码、日常更新、下载到新电脑。
> 操作前请先确认：已安装 Git for Windows，已注册 GitHub 账号。
> 全部命令在 PowerShell 中执行（`E:\8-30文档\narutto` 为项目根目录）。

---

## 1. 概念速览（先理解再操作）
- **本地仓库**：项目文件夹 + 隐藏的 `.git` 目录，记录所有版本历史。
- **远程仓库**：GitHub 上托管的副本（本项目的"云端存档"）。
- 核心流程：`git init`（本地初始化）→ `git add`（暂存）→ `git commit`（提交快照）→ `git push`（上传到 GitHub）；下载用 `git clone` / `git pull`。

## 2. 安装 Git for Windows
1. 浏览器打开 https://git-scm.com/download/win ，下载 64-bit 版本。
2. 双击安装，一路默认即可（建议默认编辑器选 Notepad 或 VS Code；"默认分支名"可选 `main`）。
3. 验证安装（新开 PowerShell）：
```powershell
git --version
```
期望输出如：`git version 2.4x.x.windows.1`

## 3. 注册 GitHub 账号
1. 打开 https://github.com/ 点击 **Sign up**。
2. 依次填写邮箱、密码、用户名，完成人机验证与邮箱验证。

## 4. 本地配置 Git 身份（只做一次）
```powershell
git config --global user.name "你的GitHub用户名"
git config --global user.email "你注册用的邮箱"
git config --global init.defaultBranch main
```
> 用户名/邮箱会显示在每次提交记录上，可随时修改。

## 5. 选择连接方式（二选一，推荐 HTTPS）
### 方式 A：HTTPS + 个人访问令牌（PAT）【推荐，最简单】
HTTPS 登录已不支持密码，需要使用令牌（Token）当作密码：
1. GitHub 右上角头像 → **Settings** → 左侧最底部 **Developer settings** → **Personal access tokens** → **Tokens (classic)**。
2. 点 **Generate new token (classic)** → Note 填 `narutto-timer` → Expiration 选 90 天或 No expiration → 勾选 **repo** 权限 → 底部 **Generate token**。
3. 立即复制生成的 `ghp_xxx` 令牌（**只显示一次**，丢失需重新生成）。
3.1.    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
4. 之后 push/clone 时提示输入用户名：填 GitHub 用户名；密码：粘贴令牌。Windows 的 Git 凭据管理器会自动记住，以后不再询问。

### 方式 B：SSH 密钥（一劳永逸，不需要每次输令牌）
1. 生成密钥（PowerShell）：
```powershell
ssh-keygen -t ed25519 -C "你注册用的邮箱"
# 一路回车即可，默认保存在 C:\Users\你的用户名\.ssh\id_ed25519
```
2. 查看公钥内容：
```powershell
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
```
3. GitHub → 头像 → **Settings** → **SSH and GPG keys** → **New SSH key** → 粘贴公钥 → 保存。
4. 测试连接：
```powershell
ssh -T git@github.com
```
看到 `Hi 用户名! You've successfully authenticated` 即成功。

## 6. 在 GitHub 创建私密仓库（只做一次）
1. GitHub 右上角 **+** → **New repository**。
2. 填写：
   - Repository name：`NaruttoTimer`
   - Description：`替身计时器：雷电模拟器菱形识别 + 15秒倒计时工具`
   - **Public / Private：选择 Private（私密）** ← 关键，只有你与被邀请的人可见
   - 其余（README、.gitignore、license）**全部不勾选**（我们本地创建并上传）
3. 点 **Create repository**。
4. 创建后页面会显示仓库地址（二选一）：
   - HTTPS：`https://github.com/你的用户名/NaruttoTimer.git`
   - SSH：`git@github.com:你的用户名/NaruttoTimer.git`

> 私密说明：GitHub 免费版支持无限私有仓库；私有仓库只有你和 Settings → Collaborators 邀请的协作者可见。

### 6.1 获取仓库网址（你已创建仓库，从这里复制）
1. 打开你的仓库页面：`https://github.com/你的用户名/NaruttoTimer`。
2. 点击页面中间的绿色 **Code（代码）** 按钮。
3. 弹出面板默认选中 **HTTPS** 标签，点击地址栏右侧的复制图标，得到类似：
   `https://github.com/你的用户名/NaruttoTimer.git`
4. 这个地址就是第 7.3 节 `git remote add origin <地址>` 中要粘贴的内容（HTTPS 方式）。

## 7. 本地初始化项目并上传（从零开始）
在项目根目录执行：

```powershell
cd E:\8-30文档\narutto
git init
```
此时已建立 `.git`（隐藏目录）。接着创建两个必填文件：

### 7.1 创建 `.gitignore`（忽略不该上传的文件）
在项目根目录新建文件，文件名就叫 `.gitignore`，内容（可直接复制）：

```gitignore
# Visual Studio 临时文件
.vs/
*.user
*.suo
*.userosscache
*.sln.docstates

# 构建结果
[Bb]in/
[Oo]bj/
[Dd]ebug/
[Rr]elease/
x64/
x86/
[Aa][Rr][Mm]/
[Aa][Rr][Mm]64/
bld/

# NuGet 包
*.nupkg
*.snupkg
packages/
*.nuget.g.props
*.nuget.g.targets

# IDE
.idea/
.vscode/
*.suo

# 本项目运行数据（识别日志/事件记录，含隐私）
data/
config/local*.json

# 本地对话记录（仅本地保留，由用户本人编写，不入库）
QA.txt

# scrcpy 原始下载包（正式组件由 vendor/scrcpy 提供）
scrcpy-win64-v4.1.zip
scrcpy-win64-v4.1/

# 日志与临时文件
*.log
*.tmp
*.temp

# 视频调试素材（录制帧较大，不入库）
*.mp4
*.mkv
```

### 7.2 创建 `README.md`（项目说明）
在项目根目录新建 `README.md`，内容模板：

```markdown
# 替身计时器（NaruttoTimer）

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
- scrcpy 4.1（vendor/scrcpy 目录）

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
- `data/` 运行数据（不入库）

## 版本
v0.1（开发中）
```

### 7.3 首次提交并上传
```powershell
cd E:\8-30文档\narutto
git status                    # 查看有哪些文件将被添加
git add .                     # 暂存所有文件（.gitignore 中的会被自动排除）
git status                    # 确认列表中没有 bin/obj/data 等
git commit -m "chore: 初始化项目结构、PRD 与开发计划"
git branch -M main            # 确保分支名为 main
git remote add origin https://github.com/你的用户名/NaruttoTimer.git
git push -u origin main
```
- 若用 SSH：`git remote add origin git@github.com:你的用户名/NaruttoTimer.git`
- 首次 push 按提示输入用户名 + 令牌（HTTPS）或自动用密钥（SSH）。
- 上传成功后，刷新 GitHub 仓库页面即可看到文件。

## 8. 日常更新（改代码后上传）
```powershell
cd E:\8-30文档\narutto
git status                    # 先看改了哪些文件
git add .                     # 或指定文件：git add src/xxx.cs
git commit -m "feat: 描述本次改动"
git push                      # 上传到 GitHub
```
规范建议：每次提交写清楚"做了什么"，如 `feat: 完成 M1 scrcpy 取流`、`fix: 修复 6 格回落误触发`。

## 9. 下载到新电脑（clone）
```powershell
cd D:\workspace
git clone https://github.com/你的用户名/NaruttoTimer.git
cd NaruttoTimer
```
之后日常更新用：
```powershell
git pull                      # 拉取远程最新改动（干活前先 pull）
```

## 10. 常用命令速查表
| 命令 | 作用 |
| --- | --- |
| `git status` | 查看当前改动状态 |
| `git log --oneline` | 查看提交历史（简洁） |
| `git add .` | 暂存全部改动 |
| `git commit -m "说明"` | 创建提交 |
| `git push` | 上传到远程 |
| `git pull` | 拉取远程更新 |
| `git clone <地址>` | 下载整个仓库 |
| `git remote -v` | 查看远程地址 |
| `git branch -M main` | 重命名当前分支为 main |
| `git diff` | 查看未暂存的具体改动 |

## 11. 常见问题
1. **push 报 `Authentication failed`**：令牌过期或凭据错误。到"控制面板 → 凭据管理器 → Windows 凭据"删除 `git:https://github.com` 条目，重新 push 并输入新令牌。
2. **push 被拒 `Updates were rejected`**：远程有本地没有的提交。先 `git pull` 合并（有冲突就解决冲突文件），再 `git push`。
3. **提示 CRLF/LF 换行符警告**：执行 `git config --global core.autocrlf true`，以后不再提示。
4. **误把大文件提交**：GitHub 单文件上限 100MB；视频/安装包不要入库（.gitignore 已排除 `*.mp4`）。
5. **`.gitignore` 不生效**：说明该文件已被跟踪过。执行 `git rm -r --cached .` 后重新 `git add .`。
6. **忘记仓库是公开还是私密**：仓库页面 → **Settings** → 底部 **Danger Zone** → **Change repository visibility** 可切换。

## 12. 本项目执行进度（2026-08-31）
已确认：UI=WPF；目标框架 net8.0-windows；按 M1~M6 多项目；连接方式 HTTPS + Token（ghp_xxx 已生成）；GitHub 私密仓库已创建；QA.txt 仅本地保留不入库。
待执行（见 6.1 与第 7 节）：
1. 复制仓库 HTTPS 网址
2. `git init` + 创建 `.gitignore`、`README.md`
3. `git add . && git commit && git remote add origin <网址> && git push -u origin main`
4. 之后每完成一个开发阶段（P1~P7）打一次提交，推送保持云端同步
