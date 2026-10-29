---
name: alife-publish
description: 当用户要求发布新版本时使用。包含完整的版本发布流程：构建（Publish\ 脚本）、打包、注册、推送、创建 GitHub Release。
---

# Alife 版本发布

当用户提供更新日志并要求发布新版本时，使用此工作流。

## 目录结构

- **主仓库**: `C:\Users\13309\Desktop\Alife` (GitHub: BDFFZI/Alife)
- **插件市场**: `C:\Users\13309\Desktop\Alife.PluginMarket` (GitHub: BDFFZI/Alife.PluginMarket)
- **插件存储**: `C:\Users\13309\Desktop\Alife.OfficialPluginStorage` (GitHub: BDFFZI/Alife.OfficialPluginStorage)
- **客户端构建输出**: `C:\Users\13309\Desktop\Shared\Alife\Outputs\Alife.Client`
- **插件构建暂存**: `C:\Users\13309\Desktop\Shared\Alife\Plugins`（`Publish\plugins.ps1` 每次运行都会**重置**此目录）
- **发布临时目录**: `C:\Users\13309\Desktop\Temp`（**可能不存在，需要先 `New-Item -ItemType Directory`**）

## 客户端运行时数据位置

排查"发布后客户端异常"时先分清数据在哪，Release 包和开发版读的不是同一个地方：

- **Release / 打包版**：插件从 `%USERPROFILE%\Documents\Alife\Storage\Plugins` 加载
- **DEBUG / 开发版**：插件直接从源码目录 `sources\Alife.Function` 加载

依据是 `sources\Alife\Alife.PluginSystem\AlifePluginSystem.cs` 里的 `#if DEBUG` 分支。

存储根目录的解析顺序（见 `sources\Alife\Alife.Foundation\AlifeContext.cs`）：
1. exe 所在包目录下的 `Storage` 子目录（存在则用，可用于做隔离测试）
2. 否则回退到 `Documents\Alife\Storage`
3. 若 `ClientConfig.json` 里写了 `storage_path`，则覆盖以上

**结论**：拿打包好的包直接测，会加载 `Documents\Alife\Storage\Plugins` 里**已安装**的插件。若那批插件版本落后，会出现大面积"编译报错/插件加载失败"，那是插件旧，不是客户端有问题——先让用户更新插件再判断。

## 工作流程

### 1. 确定版本号

**必须先检查 GitHub 上已有的 release 版本**：

```powershell
$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
gh release list --limit 5
```

**如果没有 `gh`**，用 git 直接读远端 tag（等价信息，见第 8 节）：

```powershell
git -c http.sslBackend=openssl ls-remote --tags https://github.com/BDFFZI/Alife.git |
  ForEach-Object { ($_ -split "refs/tags/")[-1] } | Where-Object { $_ -notmatch '\^\{\}' } | Sort-Object
```

**版本号规则**：

| 情况 | 升级类型 | 示例 |
|------|---------|------|
| 有新功能 (feat) | minor | 1.0.0 → 1.1.0 |
| 仅修 bug (fix) | patch | 1.0.0 → 1.0.1 |
| 新增插件 | 1.0.0 | - |
| 破坏性变更 | **必须问用户** | - |

**客户端版本号**：必须由用户指定，不要自行决定。用户给出日志但没给版本号时，要先问。

**插件版本号**：根据更新日志中的 feat/fix 情况确定，同时检查 PluginMarket JSON 中已发布的最高版本。

⚠️ **检查 PluginMarket 之前必须先 `git pull`**。本地克隆可能落后非常多（曾遇到落后 200+ 个提交），直接用本地 JSON 判断"已发布最高版本"会得出完全错误的结论。以**新克隆或已 fetch 的远端内容**为准。

⚠️ 更新日志里的插件名可能是随手写的别名而非真实插件 ID。例如日志写 `Alife.Function.DeveloperService`，但三个仓库里都不存在这个插件——`DeveloperService` 只是 `Alife.Function.Developer` 里的一个类名。**以 `sources\Alife.Function` 下的目录名和 PluginMarket 里的 JSON 文件名为准**，不确定就问用户。

### 2. 更新版本号

#### a. 客户端版本号

编辑 **`sources\Alife.Client\Alife.Client.Windows\Alife.Client.Windows.csproj`**，修改 `<Version>` 标签。

> 旧文档写的 `Sources\Alife\Alife.Client\Alife.Client.csproj` 是错的，那个文件里没有 `<Version>`。
> 自 2026-10 的「简化项目配置」提交后，此 csproj 只剩 `<Version>`；`<AssemblyName>`/`<Title>`/`<ElectronPackageId>` 均已移除，因此内部宿主程序集为项目名 `Alife.Client.Windows.exe`。

#### b. 插件 manifest.json 版本号

编辑 `sources\Alife.Function\{插件ID}\manifest.json`，修改 `"Version"` 字段。

**重要**：必须在构建之前完成，否则构建出的 ZIP 中版本号是旧的。

#### c. 检查依赖声明

**以每个 `manifest.json` 的 `Dependencies` 字段为唯一事实来源**，不要照抄下表。快速核对命令：

```powershell
Get-ChildItem "C:\Users\13309\Desktop\Alife\sources\Alife.Function" -Directory | ForEach-Object {
  $mf = Join-Path $_.FullName "manifest.json"
  if (Test-Path $mf) {
    $j = Get-Content $mf -Raw -Encoding UTF8 | ConvertFrom-Json
    $d = @($j.Dependencies.PSObject.Properties.Name)
    $depText = if ($d.Count) { $d -join ", " } else { "-" }
    "{0,-40} {1,-8} {2}" -f $_.Name, $j.Version, $depText
  }
}
```

> 本机 shell 是 **Windows PowerShell 5.1**，脚本片段要按 5.1 写。典型坑：不能把 `if` 当表达式直接放在参数位置（`-f $a, (if (...) {...} else {...})` 会报"无法将 if 项识别为 cmdlet"），要先 `$x = if (...) {...} else {...}` 再引用。

当前实际依赖（2026-10-29 核对，发布前请用上面的命令复核）：

| 插件 | 依赖 |
|------|------|
| Alife.Function.AIModelUtility | (无) |
| Alife.Function.Auditory | FunctionCaller, MessageFilter |
| Alife.Function.Auditory.SenseVoice | AIModelUtility, Auditory |
| Alife.Function.Browser | FunctionCaller |
| Alife.Function.DeskPet | FunctionCaller, MessageFilter |
| Alife.Function.Developer | FunctionCaller |
| Alife.Function.Execution | FunctionCaller |
| Alife.Function.FileService | FunctionCaller |
| Alife.Function.FunctionCaller | MessageFilter |
| Alife.Function.Language.OpenAI | FunctionCaller, TokenStatistics |
| Alife.Function.McAgent | FunctionCaller, MessageFilter |
| Alife.Function.Mcp | FunctionCaller |
| Alife.Function.Memory | FunctionCaller, MessageFilter, TokenStatistics |
| Alife.Function.MessageFilter | (无) |
| Alife.Function.ProcessService | FunctionCaller |
| Alife.Function.QChat | Speech, FunctionCaller, MessageFilter, SystemEvent |
| Alife.Function.Skill | FunctionCaller |
| Alife.Function.Speech | FunctionCaller |
| Alife.Function.Speech.EdgeTTS | Speech |
| Alife.Function.Speech.Genie | AIModelUtility, Speech |
| Alife.Function.Speech.VITS | Speech |
| Alife.Function.SystemEvent | FunctionCaller |
| Alife.Function.TimedTask | FunctionCaller, SystemEvent |
| Alife.Function.Todo | FunctionCaller, MessageFilter |
| Alife.Function.TokenStatistics | FunctionCaller |
| Alife.Function.VirtualWorld | FunctionCaller |
| Alife.Function.Vision | FunctionCaller |
| Alife.Function.Vision.MiniCPM | AIModelUtility, Vision |
| Alife.Function.Vision.OpenAI | Vision |
| Alife.Function.Vision.Qwen | AIModelUtility, Vision |
| BDFFZI.VibeCode.GameCompanion | FunctionCaller, Auditory, Vision, MessageFilter |

### 3. 构建

产物默认落在 `C:\Users\13309\Desktop\Shared\Alife\Outputs`（三个脚本通用参数 `-OutputDir` 可覆盖）。

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\13309\Desktop\Alife\Publish\client-windows.ps1"
powershell -ExecutionPolicy Bypass -File "C:\Users\13309\Desktop\Alife\Publish\client-android.ps1"
powershell -ExecutionPolicy Bypass -File "C:\Users\13309\Desktop\Alife\Publish\plugins.ps1"
```

发布脚本都在仓库根目录的 `Publish\` 下（Windows PowerShell 5.1 编写）：

- `client-windows.ps1`：publish `Alife.Client.Windows`（self-contained win-x64）到 `<OutputDir>\Alife.Client`，再用 Inno Setup（`client-windows-setup.iss`，自动从 PATH / 注册表 / 默认安装目录找 `iscc.exe`）编出安装包 `<OutputDir>\Alife.Client.Windows.exe`，版本号直接读 csproj 的 `<Version>`。
- `client-android.ps1`：publish `Alife.Client.Android`（android-arm64），APK 复制到 `<OutputDir>\Alife.Client.Android.apk`。需要 `ANDROID_SDK_ROOT`、`JAVA_HOME`、NDK clang（可用 `-JavaSdkDirectory` / `-AlifeAndroidClang` 参数覆盖）。
- `plugins.ps1`：构建全部 Function 插件，**支持 `-Plugin` 参数只构建指定插件**：

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\13309\Desktop\Alife\Publish\plugins.ps1" -Plugin "Alife.Function.SystemEvent"
```

**重要**：`plugins.ps1` 每次运行会重置整个 `Shared\Alife\Plugins` 目录。构建单个插件后目录中只保留该插件，若需打包多个插件，需逐个构建后立即打包。

注意：部分非核心 csproj 可能缺失导致报错（如 AuditoryModel、CodeService 等），不影响本次更新的插件，可忽略。

#### 平台项目解析与产物名

- `client-windows.ps1` 按约定查找平台宿主项目：优先 `sources\Alife.Client\Alife.Client.<Platform>\Alife.Client.<Platform>.csproj`，找不到再回退旧名 `Alife.Client.App.<Platform>`。**如果项目目录再次改名，脚本必须同步更新**，否则会直接抛 "Platform project not found"。
- 客户端成功标志：`Shared\Alife\Outputs\Alife.Client.Windows.exe`（Inno 安装包）存在，且解包目录 `Outputs\Alife.Client\` 内宿主 `Alife.Client.Windows.exe` 的文件版本等于本次 `<Version>`。

### 4. 创建插件 ZIP

**重要：ZIP 必须只包含源码，不能包含构建产物**

```powershell
$sharedPlugins = "C:\Users\13309\Desktop\Shared\Alife\Plugins"
$storage = "C:\Users\13309\Desktop\Alife.OfficialPluginStorage"

# 先检查源目录内容
Get-ChildItem "$sharedPlugins\{插件ID}" | ForEach-Object { Write-Host $_.Name }

# 只有这些文件是允许的：
# - *.cs（源码）
# - *.razor.g.cs（生成文件）
# - manifest.json
# - Resources/ 目录（页面资源文件，HTML/JS/CSS 等）

# 不允许的文件（如果有，必须删除后再打包）：
# - *.dll
# - *.pdb
# - *.deps.json
# - *.staticwebassets.*
```

`plugins.ps1` 的输出本身就已经是纯源码（`*.cs` 排除 `obj`、生成的 `*_razor.g.cs`、`manifest.json`、`Resources/`），构建产物都落在 `.build-validation\Publish-PluginBuild`，正常情况下不需要手工清理。但仍要**逐包验证**，见下方脚本。

**DeskPet 说明**：DeskPet 现为纯 Blazor 插件（UI 是 `DeskPetServiceUI.razor` / `Live2DDeskPetUI.razor`）；`Alife.DeskPet.Protocol` 独立项目已从 slnx 移除，协议代码收进插件目录 `Live2DDeskPet\Protocol`，直接打包源码即可，无需任何特殊 DLL 处理。

**创建 ZIP（含校验，推荐整段使用）**：

```powershell
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# 必须用正斜杠写 ZIP 条目名：Compress-Archive 和 ZipFile.CreateFromDirectory 在
# Windows 上都会把子目录写成 "sub\file.cs"，而仓库历史包用的是 "sub/file.cs"。
function New-PluginZip {
    param([string]$SourceDir, [string]$DestZip)
    if (Test-Path -LiteralPath $DestZip) { Remove-Item -LiteralPath $DestZip -Force }
    $fs = [System.IO.File]::Open($DestZip, [System.IO.FileMode]::Create)
    $ar = New-Object System.IO.Compression.ZipArchive($fs, 'Create')
    try {
        Get-ChildItem -LiteralPath $SourceDir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($SourceDir.Length + 1).Replace('\', '/')
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($ar, $_.FullName, $rel)
        }
    } finally { $ar.Dispose(); $fs.Close() }
}

foreach ($p in $plugins) {          # $plugins: @{ Id = "..."; Version = "..." }
    $src = Join-Path $sharedPlugins $p.Id
    $destDir = Join-Path $storage $p.Id
    $destZip = Join-Path $destDir "$($p.Version).zip"

    # 校验 manifest 版本与预期一致
    $mv = (Get-Content (Join-Path $src "manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json).Version
    if ($mv -ne $p.Version) { throw "$($p.Id): manifest 是 $mv，期望 $($p.Version)" }

    New-PluginZip -SourceDir $src -DestZip $destZip

    # 验证 ZIP 内容
    $zip = [System.IO.Compression.ZipFile]::OpenRead($destZip)
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    $zip.Dispose()
    $bad = @($entries | Where-Object { $_ -like "*.dll" -or $_ -like "*.pdb" -or $_ -like "*.deps.json" -or $_ -like "*staticwebassets*" })
    if ($bad.Count) { throw "$($p.Id) ZIP 含禁用项：$($bad -join ', ')" }
    $bs = @($entries | Where-Object { $_ -like '*\*' }).Count
    if ($bs) { throw "$($p.Id) ZIP 有 $bs 个条目用了反斜杠，必须改成正斜杠" }
    Write-Host "$($p.Id) -> $destZip ($($entries.Count) 项)"
}
```

ZIP 内条目平铺在根目录（不带插件名前缀），与已发布的历史包保持一致。

⚠️ **路径分隔符必须是正斜杠 `/`。** 仓库里含子目录的历史包几乎全部用 `/`（46 个），反斜杠只有 1 个历史例外。Windows 上反斜杠解包同样能正常工作（客户端走 `ZipFile.ExtractToDirectoryAsync`），所以问题不会立刻暴露；但插件正为跨平台做准备，在 Unix 上 `sub\file.cs` 会被当成文件名里带字面反斜杠，目录结构就散了。发布前用上面脚本里的反斜杠断言卡住。

### 5. 更新 PluginMarket JSON

编辑 `PluginMarket\{插件ID}.json`，在 `releases` 对象**最前面**插入新版本：

```json
"{版本号}": {
  "date": "{YYYY-M-D HH:mm:ss}",
  "note": "{更新日志，用\\n分隔}",
  "file": "https://github.com/BDFFZI/Alife.OfficialPluginStorage/raw/refs/heads/main/{插件ID}/{版本号}.zip"
}
```

**date 必须精确到分秒**（如 `2026-9-16 01:18:45`），先执行 `Get-Date -Format "yyyy-M-d HH:mm:ss"` 获取当前时间。

如果是新增插件，创建完整的 JSON 文件。

**注意**：
- 这些 JSON 文件用 UTF-8 存储，**不加 BOM**（仓库里绝大多数如此），编辑后不要引入 BOM，否则第一行会产生无谓 diff。
- 编辑后务必解析校验一次：

```powershell
$j = Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json   # 解析失败说明 JSON 写坏了
$j.releases.PSObject.Properties.Name[0]                       # 应等于新版本号
```

- `note` 里的换行必须是 JSON 转义 `\n`（两个字符），写成真实换行会导致 JSON 非法。

### 6. 提交并推送

⚠️ **先 dry-run 看清会提交什么**。工作区可能带有历史遗留状态（例如索引里存在 HEAD 中并不存在的幽灵条目、IDE 顺手改动的 Demo 文件），直接用 `git add .` 会连带提交：

```powershell
git add --dry-run .
git add .
git diff --cached --stat      # 确认改动集合与预期一致再 commit
```

```powershell
# OfficialPluginStorage
cd C:\Users\13309\Desktop\Alife.OfficialPluginStorage
git add . && git commit -m "添加插件新版本：{插件ID+版本号列表}" && git push

# PluginMarket（本地可能严重落后，先 pull --rebase）
cd C:\Users\13309\Desktop\Alife.PluginMarket
git pull --rebase
git add . && git commit -m "更新插件版本：{插件ID+版本号列表}" && git push

# 主仓库
cd C:\Users\13309\Desktop\Alife
git add . && git commit -m "v{版本号}: {简要描述}"
git push origin develop
git checkout master
git merge develop --no-edit
git push origin master
git checkout develop
```

**沙箱环境下 git 报 `schannel: AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS`**：这是受限沙箱挡了 Windows 凭据存储，不是仓库问题。切到完全权限即可恢复；临时绕过也可加 `-c http.sslBackend=openssl`。

### 7. 创建 GitHub Release

```powershell
$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
$tempDir = "C:\Users\13309\Desktop\Temp"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null   # 目录可能不存在

# 客户端发布产物（构建脚本已生成，直接上传，不要压缩）
$windowsInstaller = "C:\Users\13309\Desktop\Shared\Alife\Outputs\Alife.Client.Windows.exe"
$androidApk = "C:\Users\13309\Desktop\Shared\Alife\Outputs\Alife.Client.Android.apk"
if (-not (Test-Path $windowsInstaller)) { throw "缺少 Windows 安装包：$windowsInstaller" }
if (-not (Test-Path $androidApk)) { throw "缺少 Android 安装包：$androidApk" }

# 将更新日志写入 $tempDir\release-notes.md
# 格式：每个插件标题必须带版本号，如 ## Alife.Client v3.0.0
```

#### 7.1 确认 tag 与产物同源（关键）

**tag 必须指向"产出这两个安装包的那个提交"。** 用户在你打完 tag 之后又追加提交是常见情况（例如又提交了一次配置简化），此时按下面的顺序处理：

1. 确认包与提交内容一致——检查构建完成到提交之间有没有源文件改动：

```powershell
$buildEnd = (Get-ChildItem "C:\Users\13309\Desktop\Shared\Alife\Outputs\Alife.Client" -Recurse -File |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
Get-ChildItem "C:\Users\13309\Desktop\Alife\sources" -Recurse -File |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' -and $_.LastWriteTime -gt $buildEnd } |
  Select-Object FullName
# 无输出 + git diff HEAD 为空 => 包确实等价于当前提交
```

2. 同步 `master` 并把 tag 移到新提交：

```powershell
git checkout master && git merge develop --no-edit && git push origin master
git tag -d v{版本号} && git push origin --delete v{版本号}
git tag -a v{版本号} -m "v{版本号}" && git push origin v{版本号}
git checkout develop
git rev-list -n 1 v{版本号}     # 应等于 develop/master 的 HEAD
```

> 只要 Release 还没创建，该 tag 就还不算已发布内容，移动它是修正而非改写已发布历史。**若 Release 已经存在，不要动 tag**，改为在 release notes 里说明。

#### 7.2 创建 Release 并上传

```powershell
gh release create v{版本号} --title "v{版本号}" --notes-file "$tempDir\release-notes.md" --target master
gh release upload v{版本号} $windowsInstaller $androidApk --clobber
# 资产名必须为：Alife.Client.Windows.exe / Alife.Client.Android.apk（客户端自更新按此名查找）
```

### 8. 没有 `gh` CLI 时的备用发布流程

`gh` 可能根本没装（PATH、`C:\Program Files\GitHub CLI`、`%LOCALAPPDATA%\Programs\GitHub CLI`、WinGet Links 都查过仍没有）。此时**不必卡住**——`git push` 能成功就说明本机已存有 github.com 凭据，可直接用它调 REST API，效果与 gh 等价。

> 使用前先向用户说明会用到其已保存的 git 凭据；不要打印凭据值。

```powershell
# Windows PowerShell 5.1 默认不启用 TLS 1.2，不加这行连 github API 会直接失败
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$cred  = ("protocol=https`nhost=github.com`n`n" | git credential fill 2>&1)
$token = (($cred | Where-Object { $_ -match '^password=' }) -replace '^password=','')
$h = @{ Authorization = "Bearer $token"; 'User-Agent' = 'release-agent'; Accept = 'application/vnd.github+json' }

# 1) 核对身份与权限（scopes 需要含 repo）
Invoke-RestMethod 'https://api.github.com/user' -Headers $h
(Invoke-RestMethod 'https://api.github.com/repos/BDFFZI/Alife' -Headers $h).permissions   # push 应为 True

# 2) 确认目标版本还没有 Release（有则不要再建）
try { Invoke-RestMethod 'https://api.github.com/repos/BDFFZI/Alife/releases/tags/v{版本号}' -Headers $h }
catch { "尚无该 Release，可以创建" }

# 3) 创建 Release —— 必须用 .NET 读文件！
#    Get-Content -Raw 返回的字符串会带 PSPath 等 ETS 属性，被 ConvertTo-Json 序列化后
#    body 会变成对象，GitHub 直接 422 拒绝。
$notes = [System.IO.File]::ReadAllText("$tempDir\release-notes.md", [System.Text.Encoding]::UTF8)
$json  = [ordered]@{
  tag_name = 'v{版本号}'; target_commitish = 'master'; name = 'v{版本号}'
  body = $notes; draft = $false; prerelease = $false
} | ConvertTo-Json -Depth 3 -Compress

$rel = Invoke-RestMethod 'https://api.github.com/repos/BDFFZI/Alife/releases' -Headers $h -Method Post `
  -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -ContentType 'application/json; charset=utf-8'

# 4) 上传两个客户端安装包（走 uploads.github.com，务必放长超时/后台跑）
foreach ($asset in @($windowsInstaller, $androidApk)) {
    $name = Split-Path $asset -Leaf
    $url = "https://uploads.github.com/repos/BDFFZI/Alife/releases/$($rel.id)/assets?name=$name"
    Invoke-WebRequest -Uri $url -Headers $h -Method Post -InFile $asset -ContentType 'application/octet-stream' -TimeoutSec 3600
}

# 5) 验收
$rel2 = Invoke-RestMethod 'https://api.github.com/repos/BDFFZI/Alife/releases/tags/v{版本号}' -Headers $h
$rel2.assets | ForEach-Object { "$($_.name)  $($_.state)  $([Math]::Round($_.size/1MB,2)) MB" }
$rel2.assets | ForEach-Object { Invoke-WebRequest $_.browser_download_url -Method Head -UseBasicParsing }   # 期望 HTTP 200
```

## 发布前检查清单

- [ ] GitHub release / tag 列表已检查（无 gh 时用 `git ls-remote --tags`）
- [ ] 客户端版本号由用户确认
- [ ] 所有插件 manifest.json 版本号已更新
- [ ] 所有插件依赖声明完整（以 manifest 为准逐条核对）
- [ ] 插件版本号与 PluginMarket 已发布最高版本对比过（**且本地 PluginMarket 已 pull**）
- [ ] ZIP 内容已验证（无 .dll/.pdb/.deps.json/staticwebassets）
- [ ] PluginMarket JSON 已更新（date 精确到分秒，`releases` 最前面，无 BOM，可被解析）
- [ ] 插件市场下载链接实测可访问（HTTP 200）
- [ ] 三个仓库已提交推送，工作区干净
- [ ] Windows 安装包 `Alife.Client.Windows.exe` 与 Android 安装包 `Alife.Client.Android.apk` 均已生成
- [ ] tag 指向产出这两个安装包的提交（`git rev-list -n 1 v{版本号}` == `develop` == `master`）
- [ ] GitHub Release 已创建（非 draft、非 prerelease、target master）
- [ ] 两个安装包均已上传且 `state=uploaded`，下载链接 HTTP 200

## 常见错误

| 错误 | 原因 | 解决 |
|------|------|------|
| `Platform project not found`（client-windows.ps1） | 平台宿主项目目录被改名 | 改脚本里的项目名解析，或恢复目录名 |
| `Inno Setup not found`（client-windows.ps1） | PATH、注册表、默认安装目录都没找到 Inno Setup 6/7 | 安装 Inno Setup，或把 iscc.exe 加进 PATH |
| 插件编译失败 `CS0234` | manifest 缺少依赖 | 添加缺失的依赖 |
| 打包版大面积"插件编译报错" | `Documents\Alife\Storage\Plugins` 里是旧插件，框架 API 已变 | 让用户在客户端里更新插件，再判断是否为客户端问题 |
| `Assembly already loaded` | ZIP 包含 .dll | 清理 ZIP 重新打包 |
| 包内子目录在 Unix 上解不出来 | `Compress-Archive` 把条目名写成了反斜杠 | 用正斜杠手工建条目（见第 4 节脚本） |
| 版本号不对 | 没有检查 GitHub release | 先 `gh release list`，无 gh 用 `git ls-remote --tags` |
| 判断插件"已发布最高版本"出错 | 本地 PluginMarket 克隆严重落后 | `git pull --rebase` 后再读 JSON |
| `Get-Content -Raw` 建 Release 报 422 | 字符串带 ETS 属性被序列化成对象 | 改用 `[System.IO.File]::ReadAllText` |
| Release 的产物与 tag 不一致 | 打 tag 后用户又提交了 | 同步 master 并移动 tag 后再建 Release |
| `schannel: AcquireCredentialsHandle failed` | 受限沙箱挡了凭据存储 | 切完全权限；或 `-c http.sslBackend=openssl` |
| `Compress-Archive` 报目标目录不存在 | `Desktop\Temp` 未创建 | 先 `New-Item -ItemType Directory` |
| 脚本报"无法将 if 项识别为 cmdlet" | 本机是 Windows PowerShell 5.1，不支持参数位置的 `if` 表达式 | 拆成 `$x = if (...) {...} else {...}` |
| 连 github API 直接失败 | 5.1 默认未启用 TLS 1.2 | 先设 `[Net.ServicePointManager]::SecurityProtocol = 'Tls12'` |
