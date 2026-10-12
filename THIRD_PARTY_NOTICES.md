# Third-party components

The MIT licence in this repository covers this bridge's original code and
documentation. It does not relicense ProTubeVR's SDK, Titanfall 2, Northstar,
CircuitLord's mod, the bHaptics integration or bHaptics Player.

## ProTubeVR SDK

`ForceTubeVR_API_x64.dll` is not distributed in this repository or release ZIP.
The optional setup helper downloads it directly from the official
[ProTubeVR Unity package repository](https://github.com/ProTubeVR/ForceTubeVR-Unity-Package).
No redistribution licence was located in that repository's inspected tree on
2026-10-12. This is a record of what was found, not a claim that redistribution
is prohibited or permitted. Obtain the vendor's permission/terms before bundling
their binary in your own uploads. `.gitignore` and the release allowlist exclude it.

Pinned vendor commit: `2873daf436296d9ea84013fef41c7ff8a4b379e5`.
Path: `new/Assets/Plugins/Windows/ForceTubeVR_API_x64.dll`.
SHA-256 of the tested 164,352-byte binary:
`1f5acd7e815ad4f6289f254f8452c86732d46baefc5e975b2202d569007c5b75`.

Native API declarations were checked against the vendor's
[ForceTubeVRInterface.cs](https://github.com/ProTubeVR/ForceTubeVR-Unity-Package/blob/2873daf436296d9ea84013fef41c7ff8a4b379e5/new/Assets/Plugins/ForceTubeVRInterface.cs).
Recoil timing follows the vendor's documented
[TempoToKickPower behaviour](https://github.com/ProTubeVR/ForceTubeVR-Unreal-Engine-Plugin).

## Game dependencies and research credit

Install these separately from their respective authors. Their binaries, scripts,
assets, logs, device identifiers and user settings are not included here.

- [CircuitLordVRModInstaller](https://github.com/CircuitLord/CircuitLordVRModInstaller)
- [Northstar](https://github.com/R2Northstar)

The earlier bHaptics integration is credited as research context, not a runtime
dependency. The standalone provider emits its own events using public game APIs.

Product names identify compatibility and do not imply endorsement.
