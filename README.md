# AgentSignalDot

Windows 桌面端的 AI Agent 状态指示灯。

通过读取 Claude Code / Codex 等 Agent 会话状态，在屏幕角落显示一个实时悬浮灯，让你一眼知道当前 Agent 是在工作中、等待输入、出错还是需要审查。

## 功能特性

- **实时状态显示**：绿灯呼吸 = 工作中；黄灯 = 需审查/注意；红灯 = 阻塞/权限；蓝灯 = 就绪/完成
- **悬浮灯**：永远置顶，支持拖拽移动、锁定位置、位置记忆
- **系统托盘**：任务栏托盘图标同步显示三色信号灯
- **高饱和度颜色**：红黄绿指示灯清晰可辨
- **可锁定位置**：启动默认锁定，解锁后可自由拖拽；位置自动保存
- **支持 Claude Code / Codex hooks**：自动安装 hooks 到对应目录

## 项目结构

```
AgentSignalDot.sln
├── AgentSignalBar.Core      # 核心逻辑：信号解析、状态存储、hooks 安装
├── AgentSignalBar.Cli       # 命令行工具 agent-signal
├── AgentSignalBar.Windows   # Windows 托盘应用 + 悬浮灯窗体
└── AgentSignalBar.Tests     # 测试与自诊断
```

## 环境要求

- Windows 10 / Windows 11
- .NET 8 SDK

## 快速开始

### 1. 克隆仓库

```bash
git clone https://github.com/zhushihao/AgentSignalDot.git
cd AgentSignalDot
```

### 2. 构建并运行

```bash
dotnet build AgentSignalDot.sln -c Release
# 启动 Windows 托盘应用
dotnet run --project AgentSignalBar.Windows -c Release
```

### 3. 安装 hooks（可选）

右键系统托盘图标 → **安装/检查 Claude Code Hooks** / **安装/检查 Codex Hooks**。

安装后，Claude Code 或 Codex 在运行时会自动写入状态文件，指示灯会实时响应。

## 使用说明

| 操作 | 说明 |
|------|------|
| 左键双击悬浮灯 | 打开设置窗口 |
| 右键悬浮灯 | 打开菜单：锁定位置 / 打开设置 / 隐藏悬浮灯 |
| 取消锁定后拖拽 | 改变悬浮灯位置，松开自动保存 |
| 托盘图标右键 | 暂停/恢复监控、显示/隐藏悬浮灯、安装 hooks、退出 |

## 默认行为

- 首次启动位置：屏幕左上角 `(100, 100)`
- 启动默认锁定位置
- 窗口尺寸：`50 × 28` 像素，指示灯直径 `20` 像素

## 状态含义

| 状态 | 颜色 | 说明 |
|------|------|------|
| `working` / `thinking` | 🟢 绿色呼吸 | Agent 正在工作中 |
| `attention` / `needs_review` | 🟡 黄色呼吸 | 需要用户关注或审查 |
| `permission` / `blocked` | 🔴 红色呼吸/闪烁 | 等待权限或被阻塞 |
| `ready` / `completed` | 🔵 蓝色常亮 | 就绪或已完成 |
| `paused` | ⚪ 灰色 | 暂停监控 |

## 许可证

MIT
