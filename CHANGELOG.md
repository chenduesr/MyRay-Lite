# Changelog

## v1.8.0

### Feature Upgrade

- Added a Home connection flow card for disconnected, connecting, and connected states.
- Added Home runtime, current upload/download speed, today traffic estimate, and installed Xray Core version display.
- Added Xray Core management in Settings: installed version, latest version check, one-click core update, and geoip/geosite data update.
- Added node detail shortcuts for connect, retest, copy node name, plus endpoint, subscription source, and latest test time.
- Added Settings secondary navigation for Basic, Proxy, Routing, DNS, Advanced, and Update sections.
- Added routing rule validation before applying custom direct/proxy/block rules.
- Added log search, level filter, auto-scroll toggle, copy visible logs, and severity-colored log lines.
## v1.7.0

### UI Polish

- Refined the Home page status card so current node, delay, proxy mode, Xray status, and Windows system proxy status are shown as clear status tiles.
- Reorganized the Settings page into clearer sections: Basic, Proxy, Routing, DNS, Advanced, and Update.
- Unified key toast messages for connection, disconnection, subscription updates, and delay testing.

### Release

- Removed the v1.6.6 SHA256 release asset and in-app installer checksum requirement.
- Update downloads now use the existing direct installer download flow without requiring `sha256.txt`.

## v1.6.6

### Stability

- Added 5 MB log rotation with five retained files for app, runtime Xray, and probe logs.
- Diagnostic packages now include only the most recent retained log files.
- Batch delay tests now reserve temporary local ports before generating Xray probe configs to avoid duplicate or occupied ports.
- Port allocation failures are reported as a dedicated delay-test failure reason.

### Verification

- Added a dependency-free core test project covering subscription links, Clash YAML, Xray config generation, routing order, diagnostic redaction, log rotation, and update checksum verification.
- Release builds now run the core tests before publishing artifacts.

### Update Safety

- Release builds now publish a `sha256.txt` checksum file for the portable ZIP and Setup installer.
- In-app update downloads now require and verify the installer SHA256 before offering installation.

## v1.6.5

### Reliability and Privacy

- Diagnostic packages now structurally redact node credentials and never include an unredacted generated configuration when sanitization fails.
- Xray unexpected exits now restore the previous Windows proxy settings and update the application state.
- Windows proxy settings are backed up before MyRay Lite changes them and restored on disconnect, exit, or the next launch after an abnormal termination.
- Settings and node data are now serialized through a write lock and committed with atomic file replacement.
- Frequently edited network fields use delayed saves to avoid overlapping writes.
- Empty or unsupported subscription responses preserve the last working node cache.
- Custom block, direct, and proxy rules now take priority over built-in geographic routing rules.
- The configured startup preference is synchronized with the Windows startup registry when the app launches.

### Release

- Release builds now validate the Git tag against the project version and inject the same version into both the app and installer.
- A second launch reliably restores the existing hidden window.
- The sidebar version is read from the real published application version.

## v1.6.3

### UI and Lifecycle Fixes

- Update-check toast messages now close automatically after 5 seconds.
- Checking updates when already on the latest version no longer opens a blocking dialog.
- Clearing the subscription URL now clears the local node list, active node, and node detail drawer.
- The sidebar version now reads the published informational version so it follows app releases correctly.
- Clicking the window close button now hides MyRay Lite to the background instead of exiting immediately.
- Tray menu Exit and update installation still perform a real app exit with proxy cleanup.
- Reduced dialog and toast shadow/animation cost for smoother UI transitions.

## v1.6.2

### UI Fixes

- Fixed the Log Diagnostics toolbar layout so the description text is no longer covered by action buttons.
- Reworked the log toolbar into a two-line layout with fixed-width, centered buttons.
- Improved resilience at smaller window widths by allowing the log description to wrap cleanly.

## v1.6.1

### UI Fixes

- Fixed update-check dialogs being squeezed into the top title area.
- Fixed top-right toast notifications overlapping the window control buttons.
- Dialog and toast overlays now span the full content area with explicit z-order.

## v1.6.0

### Update Experience

- In-app update checks now detect the Setup installer asset from GitHub Releases.
- Startup update checks can remind users when a newer Release is available without downloading silently.
- The Settings page includes a startup update check toggle.
- The app downloads the newer installer into the local updates directory only after user confirmation.
- After download, the app offers an immediate silent installer launch.
- Release workflow now supports optional installer code signing when certificate secrets are configured.

### Routing

- Rule mode now explicitly routes mainland China and private traffic directly.
- `geosite:geolocation-!cn` traffic now explicitly uses the proxy outbound, improving YouTube/Google style routing.
- Rule mode now uses `IPIfNonMatch` so unmatched domains can still fall back to IP geo rules.
- Smart and blacklist templates now default unmatched traffic to proxy; whitelist defaults unmatched traffic to direct.

### App Lifecycle

- Added a single-instance guard so MyRay Lite cannot be opened twice at the same time.
- When a second launch is attempted, the app shows a clear prompt and exits the duplicate instance.

### Crash And Diagnostics

- Added local crash log collection for UI, domain, and unobserved task exceptions.
- Added a redacted diagnostic package generator.
- The Logs page now includes a diagnostics package button and copies the generated package path.
- Diagnostic packages include system info, redacted settings, redacted node summaries, recent logs, crash logs, and a redacted generated Xray config.

### UI Polish

- Added a detailed speed test progress panel with current node and result text.
- Added an editable node details drawer for common node fields such as name, address, port, transport, security, SNI, host, and path.
- Saving the active node now reconnects automatically when the proxy is running.
- Added a unified modal dialog for update, error, and diagnostics actions with an entrance animation.
- Improved dark mode coverage for logs, progress indicators, and tray menu items.

### Release And Installer

- Signed portable app binaries before zip packaging when signing certificate secrets are configured.
- Installer signing remains automatic when signing certificate secrets are configured.
- In-app installer launch now asks the installer to close and restart the app.
- Added a pending-update marker so the next launch can warn if installation did not complete and the old version is still running.

### Configuration Migration

- Added settings schema versioning.
- Older settings are backed up and migrated automatically when loaded.

## v1.5.0

### Speed Test

- Added explicit speed test modes: TCP latency, HTTP proxy latency, and download speed.
- Download mode now reads test data through the temporary Xray proxy and displays `MB/s`.
- Batch HTTP/download tests still use one temporary Xray process with multiple local ports.
- Failed nodes now keep a failure category and readable reason for UI tooltip and diagnostics.
- Retry logic now focuses on timeout and connection failures instead of blindly retrying every failure.

### Diagnostics

- Added a diagnostics service for core file checks, HTTP/SOCKS port occupancy, geo data files, and active node config generation.
- Added Xray error advice for occupied ports, invalid config, unsupported protocol, missing geo files, and Reality parameter issues.
- Xray startup failures now surface a clearer repair suggestion instead of a generic failure message.

### UI Polish

- Settings page now includes a speed test mode selector.
- Logs page now includes a one-click diagnostics button.
- Node delay cells show the current test type and expose detailed failure reasons in tooltips.
- Empty/error guidance and Toast copy were tightened around testing and diagnostics.

### Release And License

- Updated app and installer version to `1.5.0`.
- Release notes now mention installer, portable package, test modes, diagnostics, and update checking.
- Project license changed from MIT to GPL-3.0.

## v1.4.0

### Release And Installer

- Updated the Windows installer script to version `1.4.0`.
- The installer now creates a Start Menu shortcut by default.
- The installer can optionally create a desktop shortcut.
- The installer now includes an uninstall shortcut in the Start Menu group.
- GitHub Actions Release now uploads both the portable zip package and the Setup installer.
- Release notes in GitHub Actions were rewritten with clean Chinese Markdown.

### Auto Update Check

- Added an in-app GitHub Release update checker.
- The Settings page now shows the current app version.
- Clicking "检查更新" checks the latest GitHub Release and opens the download page when a newer version is available.

### UI Polish

- Added sortable node list headers for node name, delay, and status.
- Improved node list row colors for better dark mode consistency.
- Moved Toast messages to a top-right floating panel.
- Improved empty node states for both "no subscription nodes" and "no search results".
- Tightened Settings page card spacing for a more compact layout.

### Delay Test

- Added a fast TCP handshake delay pass before the heavier real-proxy HTTP probe.
- Batch tests now show delay values closer to common clients such as v2rayN.
- Nodes that fail the fast TCP pass still fall back to the temporary Xray real-proxy test.

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
