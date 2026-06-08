# MyRay Lite

MyRay Lite 是一个面向 Windows 的轻量级 Xray-core 桌面客户端，目标是把常用代理能力做得简单、清爽、可直接使用。

当前项目已经移除 v2rayN 源码依赖：不包含、不引用、不编译、不调用 v2rayN 代码。代理运行能力来自官方 Xray-core。

## 当前状态

- 平台：Windows x64
- 客户端：WPF / .NET 10
- 代理核心：Xray-core
- 项目协议：MIT
- v2rayN 依赖：无

## 本次更新

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

## 已实现功能

### 桌面客户端

- 首页、节点、订阅、日志诊断、设置页面。
- 自定义窗口、左侧导航、托盘菜单。
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

- 使用真实代理链路进行延迟测试。
- 测速时会临时启动 Xray，通过本地 HTTP 代理访问测速 URL。
- 批量测速使用临时 Xray，多端口并行测试。
- 支持分批测试和失败重试。
- 超时、启动失败、不支持协议会显示为不可用或明确提示。

### 日志诊断

- 日志诊断页面支持刷新、清空、打开日志目录。
- 记录订阅更新、Xray 启动失败、下载失败、测速失败等运行信息。
- Xray 启动失败时优先查看日志诊断页面。

### 打包与发布

- 应用图标：`src/V2RayLite.App/Assets/app.ico`
- Inno Setup 安装脚本：`installer/MyRay-Lite.iss`
- GitHub Actions Release 自动化：`.github/workflows/release.yml`
- Release 会构建 Windows x64 自包含版本，并打包官方 Xray-core 运行文件。

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

- 更完整的协议支持：TUIC、更多 Hysteria2/AnyTLS 参数、sing-box 核心可选支持。
- 更完整的节点管理：手动添加、剪贴板导入、多订阅源、订阅分组、按地区/协议/延迟筛选。
- 更强的测速能力：TCP 延迟、HTTP 延迟、下载测速、失败原因分类。
- 更完整的诊断能力：端口占用检测、Xray 配置错误解析、修复建议。
- 更细致的 UI：更统一的空状态、错误状态、弹窗和动画。
- 发布体验：正式安装包、自动检查更新、Release 页面和文件命名优化。

## 第三方组件

- Xray-core：https://github.com/XTLS/Xray-core
- Xray-core License：MPL-2.0

MyRay Lite 项目本体不包含 v2rayN 源码，也不依赖 v2rayN 运行时。

## License

MIT License
