# MyRay Lite

MyRay Lite is a Windows .NET desktop client for Xray-core with a compact UI and no v2rayN runtime/code dependency.

## Features

- Home, node list, subscription, and settings pages.
- HTTP and SOCKS local inbounds with configurable ports.
- Rule, global, and direct proxy modes.
- Subscription parsing for common `vmess`, `vless`, `trojan`, `ss`, `hysteria2`, and `tuic` links, plus base64 and Clash-style YAML subscriptions.
- Xray JSON generation and process management.
- Windows system proxy enable/disable and startup registration.
- Optional runtime download for official Xray-core Windows x64 releases.
- Log and diagnostics page for Xray startup/runtime troubleshooting.

## Third-party sources

- Xray-core is downloaded from `https://github.com/XTLS/Xray-core` and is MPL-2.0 licensed.

This project does not include, reference, compile, or call v2rayN source code.

## Release

Push a tag such as `v1.1.0` to trigger `.github/workflows/release.yml`.
The workflow builds a self-contained Windows x64 zip and bundles the latest official Xray-core runtime.

For a local portable build:

```powershell
dotnet publish src\V2RayLite.App\V2RayLite.App.csproj -c Release -r win-x64 --self-contained true -o publish\MyRay-Lite-win-x64
```

For an installer, install Inno Setup and compile `installer\MyRay-Lite.iss` after publishing.
