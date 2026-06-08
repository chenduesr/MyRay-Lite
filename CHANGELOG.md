# Changelog

## v1.3.0

### Delay Test Stability

- Changed batch delay testing to run in controlled batches instead of testing every node in one huge process.
- First pass tests up to 40 supported nodes per temporary Xray instance.
- Failed nodes are retried in smaller batches of 8 for better stability.
- Delay test completion now reports available, unavailable, and unsupported node counts.
- Unsupported protocols are skipped early with a readable reason instead of failing silently.

### Protocol Compatibility

- Added subscription parsing for `hysteria2://`, `hy2://`, `tuic://`, and `anytls://` links.
- Added Clash YAML type detection for Hysteria2, TUIC, and AnyTLS nodes.
- Added Reality parameter compatibility for common names such as `pbk`, `sid`, `fp`, `spx`, `public-key`, and `short-id`.
- Improved Clash YAML parsing for inline option maps such as `ws-opts` and nested-style fields.
- Added best-effort Xray outbound generation for Hysteria2 and AnyTLS.
- TUIC nodes are now kept in the node list with an explicit unsupported reason because Xray-core does not provide TUIC outbound support in this client path.

### UI Polish

- Improved node list column sizing for a cleaner row layout.
- Kept flag icons in the node list and refined spacing around node status, flag, delay, and action button columns.
- Disabled connect buttons for nodes that cannot be started by Xray-core.
- Added tooltips for unavailable or unsupported node reasons.
- Improved delay test toast messages so results are easier to understand.

## v1.2.0

### Tray Menu

- Added a full tray context menu.
- Supports viewing current status and active node from the tray.
- Supports opening the main window, node list, settings page, and logs page.
- Supports starting/stopping the proxy from the tray.
- Supports quick node switching from the tray menu.

### Routing Rules

- Added advanced routing settings.
- Supports bypassing mainland China domains and IP ranges.
- Supports smart, whitelist, and blacklist routing templates.
- Supports custom direct domain/IP rules.
- Supports custom proxy domain/IP rules.
- Supports custom block domain/IP rules.
- Xray routing config generation now includes these custom rules.

### DNS

- Added DNS settings to the UI.
- Supports custom DNS enable/disable.
- Supports FakeDNS enable/disable.
- Supports domestic/foreign DNS split mode.
- Supports normal DNS, DoH, and DoT server configuration.
- Xray config generation now emits DNS and FakeDNS settings when enabled.

### UI Polish

- Added dark mode.
- Added a lightweight window fade-in animation.
- Improved settings page scrolling for larger advanced configuration sections.
- Unified button, input, card, and multiline text box styling for a cleaner layout.

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
- Batch delay tests use a single temporary Xray process with one local HTTP inbound per node, then test all node ports together.

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
