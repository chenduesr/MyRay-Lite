# MyRay Lite

MyRay Lite 是一个面向 Windows 的轻量级 Xray-core 桌面客户端，目标是把常用代理能力做得简单、清爽、可直接使用。

当前项目已经移除 v2rayN 源码依赖：不包含、不引用、不编译、不调用 v2rayN 代码。代理运行能力来自官方 Xray-core。

## 当前状态

- 平台：Windows x64
- 客户端：WPF / .NET 10
- 代理核心：Xray-core
- 项目协议：GPL-3.0
- v2rayN 依赖：无

## 本次更新

### 测速与诊断

- 新增测速模式：TCP 延迟、HTTP 真实代理延迟、下载测速。
- 下载测速会通过临时 Xray 代理读取测试数据，并显示 `MB/s`。
- 节点失败会保存失败分类和修复提示，不再只显示“不可用”。
- 日志诊断页新增“一键诊断”，可检查端口占用、核心文件、geo 数据文件和当前节点配置。
- Xray 启动失败会解析常见错误，例如端口占用、配置错误、协议不支持、geo 文件缺失。
- 日志诊断页新增脱敏诊断包生成，方便排查问题。
- 应用崩溃时会在本地保存 crash 日志。

### 规则路由

- 规则模式默认中国大陆和私有地址直连，其它地区走代理。
- 明确写入 `geosite:cn` / `geoip:cn` / `geosite:private` / `geoip:private` 到直连。
- 明确写入 `geosite:geolocation-!cn` 到代理，改善 YouTube、Google 等站点的规则命中。
- 规则模式使用 `IPIfNonMatch`，未命中域名规则时继续按 IP 地理规则判断。

### Release / 安装体验

- Release 自动化同时上传便携版 zip 和正式安装包 Setup.exe。
- 安装包会创建开始菜单快捷方式，并可选择创建桌面快捷方式。
- 安装包开始菜单组包含卸载入口。
- 设置页新增版本显示和“检查更新”按钮。
- 启动时可自动检查 GitHub Release；发现新版本时弹窗提醒，不会静默下载。
- 用户确认后才会下载新版 Setup 安装包，下载完成后可立即静默安装。
- GitHub Actions 支持配置证书后自动签名安装包；未配置证书时会跳过签名正常发布。
- GitHub Actions 配置证书后也会签名便携版中的主程序。
- 应用内安装更新会尝试关闭并重启应用；如果下次启动仍是旧版本，会提示更新可能未完成。
- 旧配置会自动迁移，迁移前会备份原设置文件。

### UI 精修

- 节点列表新增表头，可按节点名称、延迟、状态排序。
- Toast 改为右上角浮层提示。
- 空状态区分“暂无节点”和“没有匹配节点”。
- 设置页分组间距更紧凑。
- 节点列表和卡片颜色继续适配暗色模式。
- 节点详情抽屉支持编辑名称、地址、端口、传输、安全、SNI、Host、Path。
- 批量测速进度会显示当前节点和即时结果。
- 弹窗新增轻量入场动画。
- 托盘菜单会跟随暗色模式切换颜色。

### 测速调整

- 批量测速新增快速 TCP 握手延迟优先路径。
- TCP 延迟数值更接近 v2rayN 等客户端常见显示。
- TCP 快速测试失败的节点会继续使用临时 Xray 做真实代理 HTTP 兜底测试。

### 测速稳定性

- 批量测速改为分批执行，避免一次性测试大量节点导致 Xray 启动失败或界面卡住。
- 首轮每批最多测试 40 个支持节点。
- 首轮失败节点会按每批 8 个再次重试。
- 测速完成后会显示可用、不可用、不支持协议的数量。
- 不支持的节点会直接显示原因，不再混在普通失败里。

### 协议兼容

- 新增订阅解析：`hysteria2://`、`hy2://`、`tuic://`、`anytls://`。
- Clash YAML 解析新增：Hysteria2、TUIC、AnyTLS 类型识别。
- Reality 参数兼容增强：支持 `pbk`、`sid`、`fp`、`spx`、`public-key`、`short-id` 等常见字段。
- Clash YAML 内联字段解析增强，例如 `ws-opts`、`reality-opts` 这类常见写法。
- Hysteria2 和 AnyTLS 已加入 Xray 出站配置生成，实际可用性取决于本地 Xray-core 版本支持情况。
- TUIC 节点会保留在列表里，但当前 Xray-core 路径不支持 TUIC 出站，界面会明确显示不支持原因。

### 界面优化

- 节点列表列宽重新整理，节点名称、延迟、按钮更规整。
- 节点列表继续使用国旗图标展示地区。
- 不支持的节点会禁用连接按钮。
- 延迟状态和连接按钮支持悬停查看失败或不支持原因。
- 测速完成提示更清楚，不再只显示简单的“测速完成”。
- 节点延迟列会显示当前测速类型，悬停可查看不可用原因。
- 日志页新增诊断入口，空状态、错误状态和提示信息更统一。
- 节点页新增测速进度条。
- 节点页新增节点详情抽屉，可查看协议、地址、端口、传输、安全、SNI、延迟和状态。
- 更新、错误和诊断反馈使用统一弹窗。
- 暗色模式覆盖日志文字和进度条等细节。

## 已实现功能

### 桌面客户端

- 首页、节点、订阅、日志诊断、设置页面。
- 自定义窗口、左侧导航、托盘菜单。
- 单实例运行：已经打开 MyRay Lite 时，再次启动会提示并退出第二个实例。
- 托盘右键菜单支持查看状态、打开页面、开关代理、快速切换节点。
- 当前代理状态、当前节点、延迟、代理模式、协议核心展示。
- 暗色模式、窗口淡入动画、统一按钮和输入框样式。

### Xray 代理能力

- 根据当前节点生成 Xray JSON 配置。
- 启动、停止并管理 `xray.exe` 进程。
- 捕获 Xray `stdout/stderr` 并写入日志文件。
- 支持本地 HTTP 和 SOCKS 入站端口。
- HTTP 默认端口：`7890`
- SOCKS 默认端口：`7891`

### 系统代理

- 开启代理时写入 Windows 系统代理。
- 关闭代理时恢复系统代理。
- 支持规则模式、全局模式、直连模式。
- 支持绕过大陆地址和大陆域名。
- 支持智能、白名单、黑名单路由模板。
- 支持自定义直连、代理、阻止的域名和 IP 规则。

### DNS

- 支持自定义 DNS。
- 支持 FakeDNS 开关。
- 支持国内外 DNS 分流。
- 支持普通 DNS、DoH、DoT 地址配置。

### 订阅与节点

- 保存订阅地址。
- 更新订阅并解析节点列表。
- 显示节点数量、上次更新时间、订阅状态。
- 支持节点搜索、选择当前节点、复制节点信息。
- 支持常见分享链接和订阅格式：
  - `vmess`
  - `vless`
  - `trojan`
  - `ss`
  - `socks`
  - `http`
  - `hysteria2`
  - `hy2`
  - `anytls`
  - `tuic`，仅解析展示，暂不支持 Xray 出站
  - Base64 订阅
  - Clash YAML `proxies` 节点

### 延迟测试

- 支持 TCP 延迟、HTTP 延迟、下载测速三种模式。
- TCP 模式直接测试节点地址端口，速度快，适合快速筛选。
- HTTP / 下载模式会临时启动 Xray，通过本地 HTTP 代理访问测速 URL。
- 批量 HTTP / 下载测速使用临时 Xray，多端口并行测试。
- 批量测速时显示进度条。
- 支持分批测试和失败重试。
- 超时、启动失败、不支持协议、响应异常会显示为不可用并保留明确原因。

### 日志诊断

- 日志诊断页面支持刷新、清空、打开日志目录。
- 支持一键诊断端口、核心文件、geo 数据文件和当前节点配置。
- 支持一键生成脱敏诊断包，路径会自动复制到剪贴板。
- 支持本地崩溃日志收集，崩溃信息保存在 `%AppData%\V2RayLite\crashes`。
- 记录订阅更新、Xray 启动失败、下载失败、测速失败等运行信息。
- Xray 启动失败时优先查看日志诊断页面。

### 打包与发布

- 应用图标：`src/V2RayLite.App/Assets/app.ico`
- Inno Setup 安装脚本：`installer/MyRay-Lite.iss`
- GitHub Actions Release 自动化：`.github/workflows/release.yml`
- Release 会构建 Windows x64 自包含版本，并打包官方 Xray-core 运行文件。
- Release workflow 支持可选代码签名。需要配置 GitHub Secrets：
  - `WINDOWS_SIGNING_CERT_BASE64`
  - `WINDOWS_SIGNING_CERT_PASSWORD`

### 更新与迁移

- 设置页“检查更新”会读取 GitHub 最新 Release。
- 设置页支持“启动时检查更新”开关。
- 如果发现新版本，会先提醒用户，确认后下载 `Setup.exe` 到 `%AppData%\V2RayLite\updates`。
- 下载完成后可点击“立即安装”静默运行安装程序。
- 设置文件带版本号，旧配置会自动备份并迁移。

## 下载使用

在 GitHub Release 页面下载 Windows x64 压缩包：

1. 下载 `MyRay-Lite-*-win-x64.zip`
2. 解压到任意目录
3. 运行 `MyRayLite.exe`

Release 包会包含：

- `MyRayLite.exe`
- `xray.exe`
- `geoip.dat`
- `geosite.dat`

用户配置、订阅链接、节点缓存会保存到本机用户目录，例如：

```text
%AppData%\V2RayLite
```

这些运行数据不会被打包进 Release，也不会上传到 GitHub。

## 本地构建

```powershell
dotnet publish src\V2RayLite.App\V2RayLite.App.csproj -c Release -r win-x64 --self-contained true -o publish\MyRay-Lite-win-x64
```

如果需要生成安装包，请先安装 Inno Setup，然后编译：

```text
installer\MyRay-Lite.iss
```

## 后续计划

- 协议兼容继续增强：TUIC 出站、更多 Hysteria2/AnyTLS 参数、可选 sing-box 核心。
- 节点管理继续增强：手动添加、剪贴板导入、多订阅源、订阅分组、批量删除和重命名。
- UI 继续精修：节点详情编辑、测速进度明细、弹窗入场动画、托盘暗色菜单。
- 发布体验继续完善：安装包签名证书接入、应用内自动重启安装、更新失败回滚提示。

## 第三方组件

- Xray-core：https://github.com/XTLS/Xray-core
- Xray-core License：MPL-2.0

MyRay Lite 项目本体不包含 v2rayN 源码，也不依赖 v2rayN 运行时。

## License

GPL-3.0 License
