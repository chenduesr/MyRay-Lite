# Changelog

## v1.1.0

### Independence

- Removed the `external/v2rayN` submodule.
- Removed `.gitmodules`.
- MyRay Lite no longer includes, references, compiles, or calls v2rayN code.
- Changed the project license to MIT.
- Updated README and third-party notices to document only the Xray-core runtime dependency.

### Subscription Parsing

- Improved subscription parsing for common links and formats.
- Supports common `vmess`, `vless`, `trojan`, `ss`, `socks`, and `http` nodes.
- Supports Base64 subscriptions.
- Supports simple Clash-style YAML `proxies` blocks.

### Delay Test

- Replaced simple TCP connection checks with real proxy delay testing.
- Delay tests now start a temporary Xray instance and access the configured test URL through a local HTTP proxy.
- Batch delay tests run with limited concurrency to avoid freezing the app or starting too many probe processes.

### Logs And Diagnostics

- Added a Logs page.
- Supports refreshing logs, clearing logs, and opening the log directory.
- Captures Xray stdout/stderr into runtime log files.
- Writes app-level diagnostics for subscription updates, Xray startup failures, downloads, and delay test failures.

### Release And Packaging

- Added a formal application icon at `src/V2RayLite.App/Assets/app.ico`.
- Added an Inno Setup installer script at `installer/MyRay-Lite.iss`.
- Added GitHub Actions Release automation at `.github/workflows/release.yml`.
- Release builds create a self-contained Windows x64 zip and bundle official Xray-core runtime files.

### Download

- Windows x64 portable package.
- Download the zip, extract it, and run `MyRayLite.exe`.
- The package includes `xray.exe`, `geoip.dat`, and `geosite.dat`.
