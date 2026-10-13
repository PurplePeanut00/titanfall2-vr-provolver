# Titanfall 2 VR ProVolver — standalone provider

ProVolver recoil for **CircuitLord's Titanfall 2 VR campaign mod**, with adaptive
rapid-fire strength.

Version **0.3.3** tracks holstered weapon ammo for immediate first shots after
switching and requests **255/255 kick power for every emitted shotgun event**.
Charge Rifle and Titan Plasma Railgun charge-up rumble is retained.
**The standalone provider, charging effect and Windows installer still need an
in-game/Windows PC test.** This remains an experimental community add-on.

## Requirements

- Windows x64, .NET Framework 4.8 with its included C# compiler.
- Titanfall 2 with [CircuitLord's VR mod](https://github.com/CircuitLord/CircuitLordVRModInstaller)
  and its Northstar runtime already installed and working.
- The included `Titanfall2VR.ProVolver` game mod.
- One ProVolver paired to Windows Bluetooth, plus the official ProTube DLL.

The earlier bridge was tested with Steam/EA, CircuitLord VR 1.0.17, Northstar
1.31.13 and one ProVolver Elite. The standalone provider targets that setup but awaits an
in-game test. Other stores, multiplayer and multiple ProTube devices are untested.

## Easy installation

1. Download the **v0.3.3 Easy Setup ZIP** and choose **Extract All**.
   GitHub's **Code -> Download ZIP** also works once the 0.3.3 source is uploaded.
2. Close Titanfall 2 and any old ProVolver bridge. Double-click **INSTALL.cmd**.
3. Setup looks for the game in your Steam libraries. Press **Enter** to confirm
   the folder, or **B** to select another. If it cannot find the game, a folder
   picker opens: select the folder containing `Titanfall2.exe` and `TF2VR`.
4. Wait for **Installed**. Setup installs the mod, builds/tests the bridge,
   downloads and checks the official ProTube DLL, saves the log path, and creates
   a desktop shortcut named **Titanfall 2 VR - ProVolver**.

Setup uses Windows PowerShell and the included .NET Framework compiler.
No Python or Visual Studio is needed. Internet access is needed on first setup
unless the exact tested DLL is placed beside INSTALL.cmd. Setup does not fire
any test kicks or connect to the ProVolver. It normally runs without elevation;
if the game folder is not writable, setup reports the access error.

## Each time you play

1. Turn on your paired ProVolver and close ProTube Companion.
2. Open **Titanfall 2 VR - ProVolver** on your desktop.
3. Launch the game through your usual VR launcher and load your campaign.

Leave the bridge window open. The shortcut starts recoil immediately, with no
test menu or log-path entry. It can wait while you launch the game. Close the
window or press Ctrl+C when you finish. It does not launch Steam/EA or select a
VR launch method for you. Your bHaptics vest setup may stay running independently.

## First test and troubleshooting tools

The provider has not yet been verified in-game. On the first run, look for
**Standalone game provider detected**, then check several single shots and a
short automatic burst. If it does not work, use the diagnostic menu:

Press **Win+R**, enter `%LOCALAPPDATA%\Titanfall2VR-ProVolver`, and open **START.cmd**.
Option 3 monitors shots without connecting to the device. Option 1 requests a
single full-power kick after you press Enter. Option 2 runs live recoil. **Option 5** runs a two-second rising rumble test
after you press Enter, without sending a recoil kick.

To verify independence, you can close bHaptics Player. You do not need to delete
your bHaptics mod; this bridge ignores its messages.

## Updates and uninstall

For an update, close the game/bridge and run the new package's INSTALL.cmd.
The installer keeps the previous app under
`%LOCALAPPDATA%\Titanfall2VR-ProVolver-backups` and previous ProVolver mod under
`TF2VR\ProVolver-backups`, outside the active mods folder. If replacing the
files fails, it attempts to restore both previous components automatically.
Other game mods are not replaced.

To uninstall, open `%LOCALAPPDATA%\Titanfall2VR-ProVolver` using Win+R and run
**UNINSTALL.cmd** with the game/bridge closed. Type REMOVE when prompted. It
moves installed files/diagnostics into backups, removes its desktop shortcut,
and restores any ProVolver mod that existed before the first installer run.
If the installed mod has been edited, it stops so you can preserve those edits.
If a game script error prevents loading, move `Titanfall2VR.ProVolver` out of
`TF2VR\mods` to disable it immediately.

## Portable/manual setup (optional)

The original manual route still works: copy `game-mod\Titanfall2VR.ProVolver`
into `TF2VR\mods`, run GET_PROTUBE_DLL.cmd, and run START.cmd. Use INSTALL.cmd
for the simpler workflow. The PLAY.cmd in the download is intended for the
installed copy and needs an already-built bridge.

## Charge-up rumble

The included provider reads the active weapon's charge fraction. The first
supported weapons are the **Charge Rifle** (`mp_weapon_defender`, while attack
is held) and **Titan Plasma Railgun** (`mp_titanweapon_sniper`, while aiming).
Those game flags still need validation with CircuitLord's VR controls.

Rumble rises from a low level toward **255/255 at full charge**, and remains at
that level while a full charge is held. The game sends an explicit stop when
charging ends, the player fires, switches weapons or dies. A local timeout stops
refreshing if updates stop (for example during a pause, load or interruption).

Every rumble request is a finite 100 ms pulse, refreshed at most 20 times/second.
The bridge expires charge state after 180 ms without a fresh received sample;
no infinite-duration command is used. These are software timing limits, not
measured Bluetooth response guarantees. Log buffering may cause stepping or
brief interruptions, so test it with the actual headset and controller setup.

Firing uses the existing ammo-based recoil detection. A charge ending **never
creates a synthetic shot**: releasing/cancelling should not create an extra kick.
A held-trigger Charge Rifle or Plasma Railgun discharge can also be detected
when charge resets and the attack cooldown advances, even without a clip decrease.
Charge cancellation by itself never produces a kick. Continuous-beam and L-STAR
heat feedback are not added.

**Run the new INSTALL.cmd even if an earlier version is installed.** Both the
bridge and game provider must be updated for the charge effect. The old 0.3.0
provider can still supply shots, but cannot supply charge state.

## Recoil behaviour

- The active pilot weapon drives your single ProVolver regardless of holding hand.
  There is no left/right device routing. Titan primary recoil and a first pulse
  for recognized Titan rocket salvos are also attempted.
- Ammo is tracked for all main weapons, including holstered weapons. A decrease
  on the active weapon emits one event per observed frame, including a switch
  and shot in the same frame. Player changes and long gaps reset the baseline.
- Actual shotguns request 255/255 kick power and retain 130 ms recovery, including
  batched events. Other weapons retain adaptive power. Cadence resets on weapon
  changes and pauses longer than 300 ms.
- Heavy groups begin with a 130 ms recovery budget; other groups begin at 60 ms.
  After two usable shot intervals, the fastest of up to six samples selects a
  50–130 ms budget with an 8 ms margin. ProTube's `TempoToKickPower` selects power.
- Slower shots can reach full power; rapid fire reduces power to allow reset.
  A single early event can wait up to 40 ms. New events replace waiting ones.
- The bridge uses the same Pistol1/channel 4 assignment as v0.2.

The provider includes a weapon name for diagnostics. The scheduler
still tunes broad recoil groups, and resets cadence when the weapon name changes. Its internal `_r` labels are legacy group names, not handedness.

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
The included GitHub Actions workflow, when uploaded under `.github`, runs these
on Windows and assembles a source-based release ZIP;
it does not launch Titanfall 2, test hardware or automatically publish anything.
`tools\Build-Release.ps1` creates the Easy Setup ZIP locally.
`tools\Test-Setup.ps1` runs installation/rollback fixture tests without hardware. The included game script
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
