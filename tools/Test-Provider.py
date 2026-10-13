#!/usr/bin/env python3
"""Execute provider logic with mocked entities in Squirrel 3.2.

Usage: python3 tools/Test-Provider.py /path/to/sq
Only Respawn type annotations/struct syntax are translated; provider control
flow and shot/charge conditions execute from the actual shipped source.
This does not validate the Respawn compiler or real game API behaviour.
"""
import pathlib
import re
import subprocess
import sys
import tempfile

root = pathlib.Path(__file__).resolve().parents[1]
source = (root / 'game-mod/Titanfall2VR.ProVolver/mod/scripts/vscripts/pvbridge_client.nut').read_text()
source = re.sub(r'^global function .*\n', '', source, flags=re.M)
match = re.search(r'struct\s*\{(.*?)\}\s*file', source, re.S)
assert match
fields = re.sub(r'\b(?:bool|string|float)\s+', '', match.group(1))
source = source[:match.start()] + 'file <- {\n' + fields + '\n}\n' + source[match.end():]
source = re.sub(r'\b(?:void|bool|string) function\b', 'function', source)
source = re.sub(r'\b(?:table<entity, (?:int|float)>|array<(?:string|entity)>|entity|bool|int|float|string)\s+(\w+)(?=\s*=)', r'local \1', source)
source = re.sub(r'\b(?:entity|bool|string)\s+(\w+)(?=\s*[,\)])', r'\1', source)
source = re.sub(r'foreach \( (?:entity|string) ', 'foreach ( ', source)
source = source.replace('thread PVBridge_Watch()', 'PVBridge_Watch()')
source = source.replace('!inventory.contains( active )', 'inventory.find( active ) == null')
harness = r'''
consolePrint <- print
messages <- []
::print = function(s) { messages.append(s) }
int <- function(v) { return v.tointeger() }
IN_ATTACK <- 1
OFFHAND_ORDNANCE <- 1
now <- 0.0
steps <- []
stepIndex <- 0
player <- null
function Time() { return now }
function IsValid(e) { return e != null }
function IsAlive(e) { return e.alive }
function GetLocalClientPlayer() { return player }
function WaitFrame() {
    if (stepIndex == steps.len()) throw "TEST_END"
    now += 0.016
    local action = steps[stepIndex++]
    action()
}
class Gun {
    name = ""; ammo = 10; attack = 0.0; charge = 0.0; ads = false
    constructor(n) { name = n }
    function GetWeaponClassName() { return name }
    function GetWeaponPrimaryClipCount() { return ammo }
    function GetNextAttackAllowedTime() { return attack }
    function GetWeaponChargeFraction() { return charge }
    function IsWeaponInAds() { return ads }
}
class Pilot {
    alive = true; titan = false; held = false; active = null; ordnance = null; inventory = null
    constructor(guns) { inventory = guns; active = guns[0] }
    function GetActiveWeapon() { return active }
    function GetMainWeapons() { return clone inventory }
    function IsTitan() { return titan }
    function IsInputCommandHeld(flag) { return held }
    function GetOffhandWeapon(slot) { return ordnance }
}
function Check(ok, name) {
    if (!ok) throw "TEST FAILED: " + name
    consolePrint("PASS " + name + "\n")
}
function Shots() {
    local count = 0
    foreach (m in messages) if (m.find("shot=") != null) count++
    return count
}
function Run(p, actions) {
    player = p; steps = actions; stepIndex = 0; now = 0.0; messages.clear()
    file.chargeActive = false; file.chargeWeapon = ""; file.nextChargeSample = 0.0
    try { PVBridge_Watch() } catch(e) { if (e != "TEST_END") throw e }
}
function Noop() {}
'''
cases = r'''
try {
    foreach (name in ["mp_weapon_semipistol", "mp_weapon_wingman", "mp_weapon_g2",
        "mp_weapon_shotgun", "mp_weapon_mastiff", "mp_weapon_shotgun_pistol",
        "mp_weapon_shotgun_doublebarrel", "mp_weapon_sniper", "mp_weapon_dmr",
        "mp_weapon_doubletake", "mp_weapon_epg", "mp_weapon_softball",
        "mp_weapon_defender", "mp_titanweapon_sniper"]) {
        local a = Gun("mp_weapon_r97"), b = Gun(name), p = Pilot([a, b])
        p.titan = name == "mp_titanweapon_sniper"
        Run(p, [Noop, function() { p.active = b; b.ammo-- }, Noop])
        Check(Shots() == 1, "switch and first shot in same frame: " + name)
        b.ammo = 10; p.active = b
        Run(p, [Noop, Noop, function() { b.ammo-- }, Noop])
        Check(Shots() == 1, "first shot after idle: " + name)
    }
    local a = Gun("mp_weapon_r97"), b = Gun("mp_weapon_wingman"), p = Pilot([a, b])
    Run(p, [Noop, function() { p.active = b }, function() { b.ammo = 12 }, Noop])
    Check(Shots() == 0, "weapon switch and reload do not kick")
    Run(p, [Noop, function() { p.alive = false; b.ammo-- },
        function() { p.alive = true }, Noop])
    Check(Shots() == 0, "respawn does not replay ammo changes")
    Run(p, [Noop, function() { now += 1.0; b.ammo-- }, Noop])
    Check(Shots() == 0, "simulation gap rebaselines without replay")
    Run(p, [Noop, function() { p.inventory = [a]; p.active = a },
        function() { b.ammo--; p.inventory = [a, b]; p.active = b }, Noop])
    Check(Shots() == 0, "newly acquired weapon has no fabricated shot")
    foreach (name in ["mp_weapon_defender", "mp_titanweapon_sniper"]) {
        local g = Gun(name), q = Pilot([g])
        q.titan = name == "mp_titanweapon_sniper"; q.held = true; g.ads = true
        Run(q, [Noop, function() { g.charge = 0.8 },
            function() { g.charge = 0.0; g.attack = now + 0.5 }, Noop])
        Check(Shots() == 1, "first energy/cooldown discharge: " + name)
        Check(messages[messages.len()-1].find("charge=0") != null,
            "discharge ends charging: " + name)
        g.attack = 0.0
        Run(q, [Noop, function() { g.charge = 0.8 },
            function() { g.charge = 0.0; g.ammo--; g.attack = now + 0.5 }, Noop])
        Check(Shots() == 1, "ammo and cooldown agree without duplicate: " + name)
        Run(q, [Noop, function() { g.charge = 0.8 },
            function() { g.charge = 0.0 }, Noop])
        Check(Shots() == 0, "charge cancellation alone never kicks: " + name)
        Run(q, [Noop, function() { g.charge = 0.8 },
            function() { g.charge = 0.0; q.held = false; g.ads = false; g.attack = now + 0.5 }, Noop])
        Check(Shots() == 0, "trigger release cancellation never kicks: " + name)
        q.held = true; g.ads = true
        local other = Gun("mp_weapon_r97")
        q.inventory.append(other)
        Run(q, [Noop, function() { g.charge = 0.8 },
            function() { q.active = other; g.charge = 0.0; g.attack = now + 0.5 }, Noop])
        Check(Shots() == 0, "switch away from charge does not kick: " + name)
    }
    local g = Gun("mp_weapon_r97"), q = Pilot([g])
    Run(q, [Noop, function() { g.ammo-- }, function() { g.ammo-- }, function() { g.ammo-- }])
    Check(Shots() == 3, "automatic ammo events remain one per firing frame")
    local titan = Pilot([Gun("mp_titanweapon_xo16")])
    titan.titan = true; titan.ordnance = Gun("mp_titanweapon_shoulder_rockets")
    Run(titan, [Noop, function() { titan.ordnance.ammo-- }, Noop])
    Check(Shots() == 1, "Titan rocket detection retained")
    consolePrint("PROVIDER_TESTS_COMPLETE\n")
} catch(e) { consolePrint(e + "\n"); throw e }
'''
with tempfile.TemporaryDirectory(prefix='pv-provider-') as tmp:
    path = pathlib.Path(tmp) / 'provider-test.nut'
    path.write_text(harness + '\n' + source + '\n' + cases)
    result = subprocess.run([sys.argv[1], str(path)], capture_output=True, text=True)
    print(result.stdout, end='')
    print(result.stderr, end='', file=sys.stderr)
    if result.returncode or 'PROVIDER_TESTS_COMPLETE' not in result.stdout:
        raise SystemExit(result.returncode or 1)
