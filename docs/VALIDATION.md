# Validation scope — 0.3.3

## Verified in this cloud environment

- All 73 hardware-free C# bridge checks pass, compiled with Roslyn using C# 5
  language rules under PowerShell 7.4.6 on Linux. Includes explicit shotgun
  255/255 output without consulting adaptive power, grouped shotgun recovery,
  weapon/idle cadence resets, charge cancellation and first discharge handling.
- Automatic-fire scheduling still produces 20 outputs from 20 events at
  1,000 RPM with alternating 5 ms arrival jitter.
- All 46 provider logic checks pass with Squirrel 3.2 and mocked game entities.
  The test reads the shipped provider source, removes Respawn type annotations,
  maps its struct to a table and array.contains to array.find, and executes its
  control flow. Cases include same-frame switching/first shots and idle first
  shots for 14 weapon names, charge/energy discharge, cancellation, no duplicate
  discharge, reload, respawn, simulation gaps, fresh inventory items, automatic
  fire and Titan rockets. This is not the Respawn compiler or actual game APIs.
- All 15 setup fixture checks pass: discovery, upgrade backups, rollback,
  changed-file detection and preserving unrelated mods.
- PowerShell scripts parse and the release builder produces the Easy Setup ZIP.
  The archive is checked for source/mod version agreement, requirement wording,
  required installer files and excluded vendor binaries/personal diagnostics.

Commands from the checkout:

```powershell
./tools/Test-Bridge.ps1
./tools/Test-Setup.ps1
./tools/Build-Release.ps1
```

Optional provider logic test (Python 3 and a Squirrel 3.2 interpreter):

```
python3 tools/Test-Provider.py /path/to/sq
```

On Windows, TEST.cmd builds and runs the bridge with .NET Framework. The cloud
Roslyn tests do not replace that Windows build check.

## Evidence and limits

The user reports that v0.3.2 installs and automatic recoil works on their PC,
with missing initial non-automatic kicks. Their SDK log reports 12 stale events,
but does not include the individual shot records or weapon identities needed to
prove which layer lost those initial kicks. Version 0.3.3 addresses the observed
switch-baseline weakness and coarse freshness boundary; it is not a confirmed
on-device resolution until retested. Stale events now include their weapon name
and age in bridge-session.txt for diagnosis.

No Titanfall executable, VR headset, Windows runtime or ProVolver is available
here. The Respawn script compiler, VR input flags, real charge/cooldown transitions,
Bluetooth receipt and physical kick strength are not verified. A 255/255 command
is maximum requested SDK power, not a measurement of physical output.

The first observed frame after spawn, load, a long simulation gap or acquiring
an unseen weapon establishes a baseline. A shot entirely before that baseline
cannot be reconstructed safely by ammo polling. Continuous beams and heat-only
weapons remain outside verified support; several shots per rendered frame and
batched logs remain coalesced. Cancellation is never itself a shot event.

## Windows acceptance for this update

1. Close game/bridge, extract the new ZIP and run INSTALL.cmd to update both parts.
2. Start with a save, fire the first shot on each semi-automatic weapon after
   waiting and after switching, then check reload/switch alone produce no kicks.
3. Test EVA-8, Mastiff and Mozambique: bridge-session.txt must show power=255
   and recoveryMs=130 for shotgun requests. Test separated shots and rapid shots.
4. Check an automatic burst still produces useful recoil.
5. Test first discharge and charging on Charge Rifle and Titan Plasma Railgun,
   including firing, release/cancel, weapon switch, death and pause. Cancel must
   not kick, discharge must kick once, and rumble must stop after discharge.
6. Check EPG and other plasma/projectile weapons independently. If a first kick
   is missing, save bridge-session.txt and matching [PVBRIDGE/1] game-log lines.

## References used

- https://docs.northstar.tf/Modding/reference/respawn/entities/
- https://github.com/R2Northstar/NorthstarMods (client GetMainWeapons usage)
- https://github.com/albertodemichelis/squirrel/tree/v3.2
- https://github.com/ProTubeVR/ForceTubeVR-Unreal-Engine-Plugin
