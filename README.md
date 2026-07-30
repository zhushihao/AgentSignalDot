# AgentSignalDot

AgentSignalDot 是一个面向 Windows 10/11 的本地 AI Agent 状态指示灯。它把 Claude Code、Codex 和 WorkBuddy（CodeBuddy）的会话状态汇总成一个始终置顶的悬浮圆点和系统托盘图标，方便快速判断 Agent 正在工作、等待授权、被阻塞，还是已经就绪。

所有状态读取、聚合和展示均在本机完成；程序不负责交易、下单，也不会把会话内容上传到外部服务。

## 功能

- `10 × 10` 像素悬浮圆点，始终置顶，不进入 `Alt+Tab`
- 默认可拖拽，支持位置锁定、位置记忆和屏幕边界约束
- 系统托盘同步显示状态，可暂停监控、隐藏悬浮灯或打开设置
- 同一 Windows 会话只允许一个托盘实例运行
- Claude Code、Codex、CodeBuddy CLI 可通过 hooks 写入状态
- WorkBuddy 桌面端可直接读取本机会话日志和心跳文件，无需安装 hooks
- WorkBuddy 权限等待、取消、进程退出和陈旧会话会自动收敛，避免红灯或绿灯长期残留
- hook 解析失败日志默认只保留异常摘要和短前导上下文，不落完整 payload

## 状态含义

| 显示 | 典型状态 | 含义 |
| --- | --- | --- |
| 🟢 绿色呼吸 | `working`、`thinking` | Agent 正在推理或执行工具 |
| 🟡 黄色呼吸 | `attention`、`needs_review`、`stale` | 需要用户关注、审查，或状态已陈旧 |
| 🔴 红色呼吸/闪烁 | `permission`、`blocked` | 正在等待授权或流程被阻塞 |
| 🔵 蓝色常亮 | `ready`、`completed`、`idle` | 就绪、完成或当前无活跃任务 |
| ⚪ 灰色 | `paused` | 已暂停监控 |

信号聚合时，阻塞和权限状态优先于普通工作状态，因此任一活跃会话需要处理时，悬浮灯会优先显示红色。

## 支持的 Agent

| Agent | 状态来源 | 是否需要安装 hooks |
| --- | --- | --- |
| Claude Code | `~/.claude/settings.json` 中的 hooks | 需要 |
| Codex | hooks 及本地 Codex session 日志 | 推荐 |
| WorkBuddy 桌面端 | `.workbuddy/logs` 与 `.workbuddy/sessions` | 不需要 |
| CodeBuddy CLI | `~/.codebuddy/settings.json` 中的 hooks | 需要 |

WorkBuddy 桌面端和 CodeBuddy CLI 是两条不同的接入路径：桌面端通过本地日志与会话文件自动感知，CLI 端通过 hooks 上报。程序同时兼容用户目录以及 `%ProgramData%\WorkBuddy\users\...` 下的沙箱数据目录。

## 环境要求

- Windows 10 或 Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 快速开始

```powershell
git clone https://github.com/zhushihao/AgentSignalDot.git
Set-Location AgentSignalDot

dotnet build AgentSignalDot.sln -c Release
dotnet run --project AgentSignalBar.Windows -c Release
```

首次启动时，圆点位于主屏幕右侧居中位置，默认未锁定。拖到需要的位置后松开鼠标即可保存；右键圆点可锁定位置。

> 如果 Release 版本已经在运行，重新构建同一输出目录可能因 DLL 被占用而失败。可先从托盘正常退出后再构建；不要直接结束进程，除非确认不再使用当前实例。

## 安装和检查 hooks

在托盘菜单或设置窗口中选择：

- **安装/检查 Claude Code Hooks**
- **安装/检查 Codex Hooks**
- **安装/检查 WorkBuddy Hooks**（用于 CodeBuddy CLI）

也可以使用命令行：

```powershell
# 仅预览，不写配置
dotnet run --project AgentSignalBar.Cli -- install-hooks --target all --dry-run --json

# 分别安装
dotnet run --project AgentSignalBar.Cli -- install-hooks --target claude
dotnet run --project AgentSignalBar.Cli -- install-hooks --target codex --codex-scope user
dotnet run --project AgentSignalBar.Cli -- install-hooks --target codebuddy
```

安装器会保留配置中的其他 hooks，并替换重复或过期的 AgentSignalDot 命令。修改前仍建议备份已有 Agent 配置。

## 使用

| 操作 | 结果 |
| --- | --- |
| 拖拽悬浮圆点 | 移动并保存位置；锁定时不可拖动 |
| 双击悬浮圆点 | 打开设置窗口 |
| 右键悬浮圆点 | 锁定位置、打开设置、隐藏悬浮灯 |
| 右键托盘图标 | 暂停/恢复监控、显示/隐藏悬浮灯、安装 hooks、退出 |

常用 CLI 命令：

```powershell
# 查看当前状态
dotnet run --project AgentSignalBar.Cli -- status --json

# 手工写入和清理状态
dotnet run --project AgentSignalBar.Cli -- working --session demo --agent manual
dotnet run --project AgentSignalBar.Cli -- reset --json

# 查看支持的信号
dotnet run --project AgentSignalBar.Cli -- list
```

CLI 还提供 `claude-hook`、`codex-hook`、`codebuddy-hook`、`workbuddy-hook` 和 `agent-hook` 子命令，通常由安装器生成的 hook 配置调用，无需手工执行。

## 本地文件与环境变量

默认聚合状态文件：

```text
%LOCALAPPDATA%\AgentSignalBar\status.json
```

可用以下环境变量覆盖路径：

| 变量 | 用途 |
| --- | --- |
| `AGENT_SIGNAL_LIGHT_STATE_FILE` | 指定完整状态文件路径 |
| `AGENT_SIGNAL_LIGHT_STATE_DIR` | 指定状态目录 |
| `SIGNAL_LIGHT_STATE_DIR` | 兼容旧版状态目录变量 |
| `AGENT_SIGNAL_WORKBUDDY_LOGS_DIR` | 指定 WorkBuddy 日志根目录 |
| `AGENT_SIGNAL_WORKBUDDY_SESSIONS_DIR` | 指定 WorkBuddy 会话目录 |

为避免意外读写系统目录，路径覆盖只接受用户目录、`LOCALAPPDATA`、临时目录、程序目录或 WorkBuddy 的受信数据根；不受信路径会被忽略或回退到默认位置。

## 开发与验证

```powershell
dotnet build AgentSignalDot.sln -c Release
dotnet run --project AgentSignalBar.Tests -c Release
```

`AgentSignalBar.Tests` 是项目自带的控制台测试入口。GitHub Actions 会在 `master` / `main` 的 push 和 pull request 上执行构建与测试。

## 项目结构

```text
AgentSignalDot.sln
├── AgentSignalBar.Core      # 信号解析、状态聚合、日志监控、hook 安装
├── AgentSignalBar.Cli       # 命令行工具 agent-signal
├── AgentSignalBar.Windows   # Windows 托盘应用与悬浮圆点
└── AgentSignalBar.Tests     # 控制台测试和回归用例
```

## 故障排查

- **没有出现悬浮灯**：确认托盘菜单中的“显示悬浮灯”已启用，并检查是否已有另一个实例在运行。
- **Claude Code / Codex 没有变色**：重新执行对应的“安装/检查 Hooks”，再从设置页刷新诊断。
- **WorkBuddy 一直蓝灯**：确认 WorkBuddy 正在运行，并检查 `.workbuddy/logs`、`.workbuddy/sessions` 或 `%ProgramData%\WorkBuddy\users\...` 是否可读。
- **状态长期不恢复**：在托盘中暂停后恢复监控，或执行 `agent-signal reset --json`。
- **构建提示文件被占用**：通常是已运行的托盘程序锁定了 Release DLL；从托盘正常退出后重试。

## 许可证

MIT
