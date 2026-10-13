# Changelog

## 0.3.3 — first-shot detection and full-power shotguns

- Uses the supplied 0.3.2 Easy Setup / Charge Up package as the base.
- Tracks all main-weapon clips, including holstered weapons, so switching and
  firing in one observed frame can emit the first shot.
- Supports a held-trigger charge-reset plus advancing-cooldown discharge on
  Charge Rifle and Titan Plasma Railgun when no clip drop is observed. Charge
  cancellation alone never emits a shot; ammo/cooldown signals do not duplicate.
- Explicit 255/255 output for actual shotguns, including grouped log events;
  shotgun recovery remains 130 ms. Other weapons keep adaptive SDK power.
- Resets adaptive cadence on weapon changes and idle gaps. The coarse log-time
  freshness window now includes its entire last second while rejecting old logs.
- Removes requirement wording from the README, installer and bridge messages.
- Adds bridge and mocked-provider regression coverage. In-game and physical
  validation of this update remains required.

## 0.3.2 — charge-up rumble (experimental)

- Adds charge-state events for Charge Rifle and Titan Plasma Railgun.
- Ramps finite ProVolver rumble pulses to full power as charge increases.
- Cancels on charge release, firing, weapon swap, death and missing updates.
- Adds diagnostic menu option 5 for a two-second rumble-only test.
- Keeps the existing recoil scheduler and avoids synthesizing shots on cancellation.
- Adds 17 bridge tests. Hardware and game-script validation are still required.

## 0.3.1 — guided setup

- INSTALL.cmd finds Steam game libraries, offers a folder picker, installs the
  provider, builds/tests the bridge and downloads the verified ProTube DLL.
- Saves the logs path and creates a desktop shortcut for direct live recoil.
- Adds backups, rollback on replacement failure and an uninstall helper.
- Includes 15 setup fixture tests; Windows UI and complete install still need testing.
- Game provider and recoil behaviour are unchanged from 0.3.0.

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
