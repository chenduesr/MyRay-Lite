# MyRay Lite

MyRay Lite 是一个面向 Windows x64 的轻量级 Xray-core 桌面客户端。它把常用代理能力、订阅管理、节点测速、日志诊断和更新流程放在一个简洁的 WPF 界面里，适合希望“打开即可用”的桌面用户。

> MyRay Lite 使用官方 XTLS/Xray-core 作为代理运行核心。项目不包含、不引用、不编译、不调用 v2rayN 源码。

## 当前状态

- 平台：Windows x64
- 客户端：WPF / .NET 10
- 代理核心：XTLS/Xray-core
- 开源协议：GPL-3.0
- Xray-core 协议：MPL-2.0
- 当前版本：v1.8.0

## 主要功能

### 代理与系统集成

- 支持系统 HTTP / SOCKS 代理。
- 支持规则模式、全局模式、直连模式。
- 自动生成 Xray JSON 配置并启动、停止、监控 `xray.exe`。
- Xray 异常退出后会恢复系统代理，避免系统代理指向失效端口。
- 支持开机自启、启动后自动连接、最小化到托盘、单实例唤醒。

### 订阅与节点

- 支持常见分享链接、Base64 订阅和 Clash YAML。
- 支持 VMess、VLESS、Trojan、Shadowsocks、Socks、HTTP、Hysteria2、AnyTLS 等常见节点解析。
- 支持节点搜索、选择、连接、批量测速、排序和国旗图标。
- 节点详情可查看协议、地址、端口、传输、安全参数、最近测速结果和失败原因。

### 测速与诊断

- 支持 TCP 延迟、真实 HTTP 延迟和下载测速。
- 批量测速使用临时 Xray 多端口方式，支持分批、重试和进度显示。
- 日志页支持搜索、等级筛选、自动滚动、复制可见日志和错误高亮。
- 支持一键诊断和脱敏诊断包，便于排查端口占用、核心缺失、配置错误等问题。

### 路由、DNS 与更新

- 默认规则：大陆和局域网直连，其他地区按当前模式走代理。
- 自定义阻断、直连、代理规则优先于内置地域规则。
- 支持自定义 DNS、FakeDNS、国内外分流、DoH 和 DoT。
- 设置页支持检查应用更新、下载新版安装包。
- v1.8.0 起支持查看 Xray Core 当前版本、检查最新版、一键更新核心和更新 geoip/geosite 数据。

## v1.8.0 更新亮点

- 首页新增连接流程状态：未连接、连接中、已连接。
- 首页新增连接时长、实时上传/下载速度、今日流量估算和 Xray Core 版本显示。
- 节点详情新增快捷操作：连接、重新测速、复制名称。
- 设置页新增二级导航：基础、代理、路由、DNS、高级、更新。
- 路由规则新增保存前格式校验。
- 日志页新增搜索、等级筛选、自动滚动、复制和高亮。
- 设置页新增 Xray Core 管理和 geoip/geosite 数据更新入口。

> 首页流量统计基于 Windows 网卡计数器估算，用于快速观察连接状态；它不是 Xray 内部精确分流统计。

## 下载使用

请到 GitHub Release 页面下载最新版本：

- 安装版：`MyRay-Lite-v*-Setup.exe`
- 便携版：`MyRay-Lite-v*-win-x64.zip`

便携版解压后运行 `MyRayLite.exe` 即可。Release 包会包含：

- `MyRayLite.exe`
- `xray.exe`
- `geoip.dat`
- `geosite.dat`

用户配置、订阅地址、节点缓存和日志会保存在本机用户目录，例如：

```text
%AppData%\V2RayLite
```

这些运行数据不会被打包进 Release，也不会上传到 GitHub。

## 本地构建

```powershell
dotnet publish src\V2RayLite.App\V2RayLite.App.csproj -c Release -r win-x64 --self-contained true -o publish\MyRay-Lite-win-x64
```

如果需要生成安装包，请先安装 Inno Setup，然后使用仓库内脚本：

```text
installer/MyRay-Lite.iss
```

正式 Release 由 GitHub Actions 自动构建，会同时发布便携 ZIP 和 Setup 安装包。

## 第三方说明

- Xray-core：https://github.com/XTLS/Xray-core
- Xray-core License：MPL-2.0
- MyRay Lite License：GPL-3.0

详细说明见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。
