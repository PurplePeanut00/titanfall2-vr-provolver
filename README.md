# Titanfall 2 VR ProVolver — standalone provider

ProVolver recoil for **CircuitLord's Titanfall 2 VR campaign mod**, with adaptive
rapid-fire strength. **No bHaptics plugin, bHaptics Player or bHaptics hardware is
required by this version.**

Version **0.3.0 is an experimental first-test build**. A small included Northstar
client script observes the player's weapons and writes its own shot events.
The separate Windows bridge reads those events and drives the ProVolver.
The recoil scheduler and ProTube driver are unchanged from the successful v0.2
test, but this new game script has **not yet been compiled or tested in-game**.
This is an unofficial community add-on, not native support in CircuitLord's mod.

## Requirements

- Windows x64, .NET Framework 4.8 with its included C# compiler.
- Titanfall 2 with [CircuitLord's VR mod](https://github.com/CircuitLord/CircuitLordVRModInstaller)
  and its Northstar runtime already installed and working.
- The included `Titanfall2VR.ProVolver` game mod.
- One ProVolver paired to Windows Bluetooth, plus the official ProTube DLL.

The earlier bridge was tested with Steam/EA, CircuitLord VR 1.0.17, Northstar
1.31.13 and one ProVolver Elite. Version 0.3.0 targets that setup but awaits an
in-game test. Other stores, multiplayer and multiple ProTube devices are untested.

## Installation and first test

1. Close Titanfall 2 and any older ProVolver bridge window.
2. Extract the Windows release ZIP into a writable folder outside the game.
3. Copy **`game-mod\Titanfall2VR.ProVolver`** into **`Titanfall2\TF2VR\mods`**.
   The result must be `TF2VR\mods\Titanfall2VR.ProVolver\mod.json`.
   Copy the entire folder, not just `mod.json`. Do not replace your entire mods folder.
4. Run **GET_PROTUBE_DLL.cmd** once. This fetches the pinned official SDK and
   verifies its SHA-256 checksum. Alternatively copy the tested
   `ForceTubeVR_API_x64.dll` from your older working bridge next to `START.cmd`.
5. Turn on your paired ProVolver and close ProTube Companion.
6. Launch the game normally in VR and load a campaign save.
7. Run **START.cmd**. It compiles the bridge locally; no Visual Studio or Python
   is required. Choose **3 — Monitor shots only** first. If prompted, paste the
   full path to your game's `TF2VR\logs` folder.
8. Wait for **Standalone game provider detected** (normally within five seconds
   of active gameplay). Fire several separated shots. The `Shots` count should
   increase; reloading or switching weapons by itself should not add shots.
9. Stop the monitor with Ctrl+C, restart START.cmd if the terminal closes, then
   choose **2 — Start live recoil bridge**. Test single shots, bursts and rapid fire.

Option **1** requests one full-power test kick after the connection vibration
and your Enter keypress. Q then Enter cancels it. Option 3 never loads the SDK.
The bridge saves your log folder in `logs-path.txt`; delete/edit it to change it.

You may keep bHaptics installed for your vest: this bridge **ignores its events**.
To verify independence, close bHaptics Player for this test. If desired, with the
game closed, temporarily move `Titanfall2VR_bhaptics` out of `TF2VR\mods` and its
bHaptics DLL out of `TF2VR\plugins` into a backup folder. Do not move unrelated
plugins. Removing bHaptics is optional, not part of normal setup.

If the new script prevents the game loading, close the game and move only
`Titanfall2VR.ProVolver` out of `TF2VR\mods`. Your earlier bridge remains usable
with its original bHaptics setup.

## Recoil behaviour

- The active pilot weapon drives your single ProVolver regardless of holding hand.
  There is no left/right device routing. Titan primary recoil and a first pulse
  for recognized Titan rocket salvos are also attempted.
- A decrease in magazine ammo emits one event per observed frame. Reloads, weapon
  switches, player changes and long simulation gaps establish a new baseline.
- Heavy groups begin with a 130 ms recovery budget; other groups begin at 60 ms.
  After two usable shot intervals, the fastest of up to six samples selects a
  50–130 ms budget with an 8 ms margin. ProTube's `TempoToKickPower` selects power.
- Slower shots can reach full power; rapid fire reduces power to allow reset.
  A single early event can wait up to 40 ms. New events replace waiting ones.
- The bridge uses the same Pistol1/channel 4 assignment as v0.2.

The provider includes a weapon name for diagnostics. The preserved scheduler
still tunes broad recoil groups, so switching within a group can take a few
shots to retune. Its internal `_r` labels are legacy group names, not handedness.

## Limits

Magazine polling is not a universal weapon-fire callback. Charge weapons,
infinite-ammo weapons, continuous beams and scripted ammo changes may not be
represented correctly. Several shots inside one rendered frame produce one
event. Rocket salvo detection is heuristic and needs testing with each Titan.
Do not claim every weapon is supported.

This remains a log bridge: file buffering can delay/batch events. It skips
historical entries, stale events and large backlogs, and never replays a burst
of old shots. Multiple newly delivered events result in at most one pending kick.
The software ceiling of 20 output requests/second is not a physical guarantee.
The heartbeat shows the script/logger is active, not that Bluetooth is connected.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Game reports a script compile error | Photograph the full error, remove the new provider folder to recover, and report it. The game compiler is still untested. |
| No provider detected | Confirm `TF2VR\mods\Titanfall2VR.ProVolver\mod.json` exists; fully restart and load the campaign. Verify your logs path. |
| Provider detected but no shots | Try a normal magazine weapon such as a rifle. Check the newest log for `[PVBRIDGE/1] shot=`. |
| Test kick works but gameplay does not | Use monitor mode and check that Shots increases before checking Bluetooth again. |
| Missing DLL | Run GET_PROTUBE_DLL.cmd or copy the DLL from the earlier working bridge. |
| DLL load error 126 | Check the SDK and Microsoft Visual C++ x64 runtime dependencies. |
| Another bridge is running | Close the old window; versions share a single-instance lock. |
| Rapid-fire glitches | Save `bridge-session.txt` before restarting; its grouped/expired/pause counters help diagnose delays. |

Each run replaces `bridge-session.txt`. Remove personal paths before sharing.
For a parser problem, include just a few `[PVBRIDGE/1]` game-log lines.

## Build, test and release

`BUILD.cmd` compiles the C# bridge. `TEST.cmd` runs its hardware-free tests.
GitHub Actions runs these on Windows and assembles a source-based release ZIP;
it does not launch Titanfall 2, test hardware or automatically publish anything.
`tools\Build-Release.ps1` creates the same ZIP locally. The included game script
is compiled by Titanfall/Northstar at load time, not by BUILD.cmd or TEST.cmd.
See [validation notes](docs/VALIDATION.md).

The SDK helper downloads from the official [ProTubeVR repository](https://github.com/ProTubeVR/ForceTubeVR-Unity-Package)
at pinned commit `2873daf436296d9ea84013fef41c7ff8a4b379e5`, path
`new/Assets/Plugins/Windows/ForceTubeVR_API_x64.dll`.
It will not overwrite a different existing DLL. Its PowerShell execution-policy
option applies only to that invocation, not your saved Windows settings.

## Credits and licence

CircuitLord created the VR mod; Northstar supplies the scripting/runtime platform;
ProTubeVR supplies the device and SDK. floh-bhaptics' integration helped establish
the earlier bridge's magazine-based approach. This standalone provider uses
ordinary game scripting APIs and emits its own protocol; no bHaptics code,
binaries, patterns or services are bundled or called.

This project's original code and documentation use the [MIT licence](LICENSE).
Third-party software retains its own terms; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
No endorsement by the game, mod or hardware developers is implied.
