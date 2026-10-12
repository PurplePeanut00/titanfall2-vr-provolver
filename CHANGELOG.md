# Changelog

## 0.3.0 — standalone provider (experimental)

- Bundles a campaign-only Northstar script for magazine-based shot detection and
  heuristic Titan rocket salvos. No bHaptics dependency.
- Uses the versioned PVBRIDGE/1 log protocol with weapon names and a heartbeat.
- Ignores bHaptics events, allowing independent coexistence with vest haptics.
- Keeps the tested v0.2 recoil scheduler and ProTube native driver unchanged.
- Supports the active pilot weapon regardless of hand on one ProVolver.
- Adds parser/heartbeat regression tests. In-game validation still required.

## 0.2.1

Public packaging of the successful v0.2 bHaptics-log bridge, with SDK download
helper, user-selected logs path, documentation and GitHub CI.
