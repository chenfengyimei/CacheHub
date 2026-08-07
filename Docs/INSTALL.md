# CacheHub 安装手册

> 本文档面向人类用户和 AI Agent。AI Agent 可直接阅读本文件，按步骤执行即可完成 CacheHub 的全自动安装。

---

## 前置条件

| 条件 | 要求 |
|------|------|
| 操作系统 | Windows 10+ / Linux / macOS |
| .NET SDK | 10.0.302 或更高（`global.json` 锁定 10.0.302，`rollForward: latestPatch`） |
| Git | 任意版本（用于克隆仓库和 `repo` 命令） |
| 磁盘空间 | ~200MB（源码 + 构建 + 发布） |

### 安装 \.NET 10 SDK

如果尚未安装 \.NET 10 SDK：

- **Windows**：从 https://dotnet.microsoft.com/download/dotnet/9.0 下载安装程序
- **Linux**（Ubuntu）：
  ```bash
  wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
  chmod +x dotnet-install.sh
  ./dotnet-install.sh --channel 9.0
  export PATH="$HOME/.dotnet:$PATH"
  ```
- **macOS**（Homebrew）：
  ```bash
  brew install --cask dotnet-sdk
  ```

验证：
```bash
dotnet --version
# 输出应 >= 10.0.302
```

---

## 安装方式

### 方式一：一键脚本安装（推荐）

```bash
# 1. 克隆仓库
git clone https://github.com/chenfengyimei/CacheHub.git
cd CacheHub

# 2. 运行安装脚本
# Windows (PowerShell):
./install.ps1

# Linux / macOS (Bash):
chmod +x install.sh
./install.sh
```

安装脚本会自动完成：构建 → 测试 → 发布单文件可执行文件到 `./publish/` 目录。

### 方式二：手动安装

```bash
# 1. 克隆仓库
git clone https://github.com/chenfengyimei/CacheHub.git
cd CacheHub

# 2. 构建
dotnet build CacheHub.sln -c Release

# 3. 运行测试（可选但推荐）
dotnet test CacheHub.sln -c Release --no-build

# 4. 发布单文件可执行文件
dotnet publish src/CacheHub.Cli/CacheHub.Cli.csproj -c Release -o ./publish
```

### 方式三：从源码直接运行（开发模式）

```bash
git clone https://github.com/chenfengyimei/CacheHub.git
cd CacheHub
dotnet run --project src/CacheHub.Cli -- version
```

---

## 配置 PATH

安装后需要将 `publish/` 目录加入系统 PATH 才能在任意位置使用 `cachehub` 命令。

### Windows (PowerShell)

```powershell
# 当前会话生效
$env:PATH += ";$PWD\publish"

# 永久生效（用户级）
[Environment]::SetEnvironmentVariable("PATH", $env:PATH + ";$PWD\publish", "User")
```

### Linux / macOS

```bash
# 当前会话生效
export PATH="$PATH:$(pwd)/publish"

# 永久生效
echo 'export PATH="$PATH:'$(pwd)/publish'"' >> ~/.bashrc
source ~/.bashrc
```

---

## 验证安装

```bash
# 1. 检查版本
cachehub version

# 2. 查看能力
cachehub capabilities

# 3. 完整安装验证（5 步检查）
cachehub integration verify
```

`integration verify` 检查项：
1. 数据目录可访问
2. 数据库和迁移已应用
3. 工作区仓储可用
4. CLI 功能正常
5. 回滚能力（安全移除）

---

## Gitee 镜像（国内用户）

如果 GitHub 访问慢，可使用 Gitee 镜像：

```bash
git clone https://gitee.com/chenfengyimei/CacheHub.git
cd CacheHub
```

---

## AI Agent 自动安装指令

> 以下内容专为 AI Agent 设计。Agent 读取本节后可直接执行安装。

### 环境检测

```bash
# 检测 .NET SDK
dotnet --version

# 检测 Git
git --version
```

如果 `dotnet` 不存在，提示用户安装 \.NET 10 SDK 后继续。

### 自动安装流程

```bash
# Step 1: 克隆仓库
git clone https://github.com/chenfengyimei/CacheHub.git
cd CacheHub

# Step 2: 构建（确保编译通过）
dotnet build CacheHub.sln -c Release

# Step 3: 运行测试（确保功能正常）
dotnet test CacheHub.sln -c Release --no-build --verbosity quiet

# Step 4: 发布单文件可执行文件
dotnet publish src/CacheHub.Cli/CacheHub.Cli.csproj -c Release -o ./publish

# Step 5: 配置 PATH（Windows PowerShell）
# $env:PATH += ";$PWD\publish"
# 或（Linux/macOS）
# export PATH="$PATH:$(pwd)/publish"

# Step 6: 验证安装
./publish/cachehub version
./publish/cachehub capabilities
./publish/cachehub integration verify
```

### 安装后验证清单

- [ ] `cachehub version` 输出版本号
- [ ] `cachehub capabilities` 输出能力列表
- [ ] `cachehub integration verify` 全部通过
- [ ] `cachehub workspace import <path>` 可导入项目
- [ ] `cachehub index build --id=<id>` 可构建索引
- [ ] `cachehub context build --workspace=<id> --task="test"` 可构建上下文

### Web UI 启动（可选）

```bash
dotnet run --project src/CacheHub.Desktop
# 访问 http://localhost:5099
# API Token 打印在终端中，所有 /api/ 请求需 Authorization: Bearer <token>
```

---

## 数据目录

CacheHub 的数据存储位置：

| 操作系统 | 路径 |
|----------|------|
| Windows | `%LOCALAPPDATA%\CacheHub\` |
| Linux | `~/.local/share/CacheHub/` |
| macOS | `~/Library/Application Support/CacheHub/` |

目录结构：
```
CacheHub/
├── workspaces.db          # SQLite 数据库（工作区 + 索引 + FTS5 + 上下文包 + 反馈）
└── exports/               # 文件导出目录
```

---

## 卸载

```bash
# 1. 删除发布目录
rm -rf ./publish

# 2. 删除数据目录（可选，会清除所有工作区和上下文）
# Windows: Remove-Item -Recurse "$env:LOCALAPPDATA\CacheHub"
# Linux:   rm -rf ~/.local/share/CacheHub
# macOS:   rm -rf ~/Library/Application\ Support/CacheHub

# 3. 从 PATH 移除 publish 目录
```

---

## 常见问题

### Q: 构建时报 "SDK version not found"

确保已安装 \.NET 10 SDK。`global.json` 指定版本 10.0.302，`rollForward: latestPatch` 允许使用更高补丁版本。

### Q: 测试中有 2 个跳过

这是正常的。2 个跳过的测试 (`GitDiffProviderTests`) 需要真实 Git 仓库环境，在 CI 中会自动跳过。

### Q: `cachehub` 命令找不到

确保 `publish/` 目录已加入 PATH。可用完整路径 `./publish/cachehub` 替代。

### Q: Web UI 端口被占用

```bash
dotnet run --project src/CacheHub.Desktop -- --urls=http://localhost:5001
```
