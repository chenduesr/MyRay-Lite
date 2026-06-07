# MyRay Lite

MyRay Lite 是一个面向 Windows 的轻量级 Xray-core 桌面客户端，目标是把常用代理能力做得简单、清爽、可直接使用。

当前版本已经移除 v2rayN 代码依赖，项目本体采用 MIT License；运行代理能力来自官方 Xray-core。

## 当前状态

- 平台：Windows x64
- 客户端：WPF / .NET
- 代理核心：Xray-core
- 项目协议：MIT
- v2rayN 依赖：无，不包含、不引用、不编译、不调用 v2rayN 源码

## 已实现功能

### 桌面客户端

- 首页、节点、订阅、日志诊断、设置等主要页面。
- 自定义窗口与左侧导航布局。
- 当前代理状态、当前节点、延迟、代理模式、协议核心展示。
- 托盘右键菜单：查看状态、打开页面、开关代理、快速切换节点。
- 暗色模式、窗口淡入动画、统一按钮状态和滚动设置页。

### Xray 代理能力

- 根据当前节点生成 Xray JSON 配置。
- 启动、停止并管理 `xray.exe` 进程。
- 捕获 Xray `stdout/stderr` 并写入日志。
- 支持本地 HTTP 和 SOCKS 入站端口。
- HTTP 默认端口：`7890`
- SOCKS 默认端口：`7891`

### 系统代理

- 开启代理时写入 Windows 系统代理。
- 关闭代理时恢复系统代理。
- 支持规则模式、全局模式、直连模式的基础切换。
- 支持更细的路由规则设置：
  - 绕过大陆地址和大陆域名
  - 白名单规则
  - 黑名单规则
  - 自定义直连域名/IP
  - 自定义代理域名/IP
  - 自定义阻止域名/IP

### DNS

- 支持自定义 DNS。
- 支持 FakeDNS 配置开关。
- 支持国内外 DNS 分流。
- 支持常规 DNS、DoH、DoT 地址配置。

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
  - Base64 订阅
  - 简单 Clash YAML `proxies` 节点

### 延迟测试

- 使用真实代理链路进行延迟测试。
- 测速时会临时启动 Xray，通过本地 HTTP 代理访问测速 URL。
- 批量测速限制并发数量，避免界面卡死或同时启动过多测试进程。
- 超时或失败节点会显示不可用状态。

### 日志诊断

- 新增日志诊断页面。
- 支持刷新日志、清空日志、打开日志目录。
- 记录订阅更新、Xray 启动失败、下载失败、测速失败等运行信息。

### 打包与发布

- 已加入正式应用图标：`src/V2RayLite.App/Assets/app.ico`
- 已加入 Inno Setup 安装脚本：`installer/MyRay-Lite.iss`
- 已加入 GitHub Actions Release 自动化：`.github/workflows/release.yml`
- Release 会构建 Windows x64 自包含版本，并打包官方 Xray-core 运行文件。

## 后续计划

### 路由与 DNS

- 规则编辑体验继续精细化。
- 规则导入、导出和预设模板。
- 更完整的 DNS 规则校验。
- DNS 连通性测试。

### 节点管理

- 节点详情页。
- 手动添加节点。
- 从剪贴板导入节点。
- 导入、导出当前配置。
- 多订阅源和订阅分组。
- 按订阅、协议、地区、延迟筛选节点。

### 测速与诊断

- TCP 延迟、HTTP 延迟、下载测速。
- 更明确的失败原因展示。
- 端口占用检测。
- Xray 配置错误解析。
- 更完整的错误诊断和修复建议。

### 桌面体验

- 开机自启、启动后自动连接、最小化到托盘体验优化。
- 更细致的 UI 动画和状态反馈。

### 发布体验

- 正式 `.exe` 安装包。
- 桌面快捷方式、开始菜单、卸载程序。
- 自动检查新版本。
- Release 页面说明和下载包命名优化。

### 高级能力

- TUN 模式。
- 更复杂的 DNS/路由处理。
- 管理员权限检测和引导。
- 本地配置加密。
- 订阅链接脱敏显示。
- 敏感日志过滤。

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

## 第三方组件

- Xray-core：<https://github.com/XTLS/Xray-core>
- Xray-core License：MPL-2.0

MyRay Lite 项目本体不包含 v2rayN 源码，也不依赖 v2rayN 运行时。

## License

MIT License
