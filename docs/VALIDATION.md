# Validation scope — 0.3.0

## What passed

- C# bridge compilation with C# 5 language rules, using Roslyn in PowerShell on Linux.
- 33 hardware-free bridge tests covering the standalone protocol, malformed input,
  bHaptics rejection, heartbeat handling, partial writes, truncation, old logs,
  stale events, grouped shots and preserved recoil scheduling.
- Rapid-fire simulation: 20 of 20 requests with 1,000 RPM input and alternating
  5 ms arrival jitter. This measures software scheduling, not physical kicks.
- The Scheduler and Driver classes are identical to the successful v0.2.1 package.
- PowerShell packaging scripts parsed and the release builder executed.
- Mod manifest and callback/path relationships checked; game APIs and script
  syntax patterns reviewed against the supplied mod installation and Northstar docs.
- Public packages exclude vendor binaries, personal paths, logs and device IDs.

## What has not been tested

The **new game script has not been compiled or run in Titanfall 2**. The Respawn
Squirrel VM/compiler is not available in this environment. Static inspection
and passing C# tests are not a substitute for game compilation or runtime testing.
Windows .cmd scripts and the GitHub Windows workflow have not been executed here.
No claim is made that this standalone build works on hardware yet.

The earlier v0.2 bridge was tested successfully with one ProVolver Elite and a
selection of weapons, using bHaptics events. That result supports retaining its
scheduler/driver, not assuming that the new event provider is already validated.

## In-game acceptance

1. Install the new game-mod folder and load a campaign save without script errors.
2. With bHaptics Player closed, run menu 3. Confirm the provider heartbeat appears.
3. Check Shots increases for single shots and rapid fire, but not just on reload,
   weapon switching, death/respawn or loading a save. Test an ordinary rifle first.
4. Run menu 2 and check physical recoil timing on several pilot weapons.
5. Test Titan primary weapons and shoulder rockets separately; record gaps.
6. Optionally keep bHaptics enabled for the vest and confirm no duplicated kicks.

If a check fails, report the exact error or relevant protocol lines, weapon,
mod versions and redacted bridge-session.txt. Keep the older working package
until this version has passed in-game checks. Release as a pre-release meanwhile.

## Technical references

- https://docs.northstar.tf/Modding/guides/gettingstarted/
- https://docs.northstar.tf/Modding/reference/respawn/entities/
- https://docs.northstar.tf/Modding/squirrel/async/
