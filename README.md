# V2Ray Lite

V2Ray Lite is a Windows .NET desktop client for Xray-core with a compact UI based on the provided mockups.

## Features

- Home, node list, subscription, and settings pages.
- HTTP and SOCKS local inbounds with configurable ports.
- Rule, global, and direct proxy modes.
- Subscription parsing for common `vmess`, `vless`, `trojan`, and `ss` links.
- Xray JSON generation and process management.
- Windows system proxy enable/disable and startup registration.
- Optional runtime download for official Xray-core Windows x64 releases.

## Third-party sources

- `external/v2rayN` is tracked as a Git submodule from `https://github.com/2dust/v2rayN` for GPL-3.0-compatible reference and future reuse.
- Xray-core is downloaded from `https://github.com/XTLS/Xray-core` and is MPL-2.0 licensed.

Because v2rayN source is included as a submodule for reuse/reference, distribute this project under GPL-3.0-compatible terms.
