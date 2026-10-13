// Original standalone provider. See LICENSE. Client campaign only.
// Emits protocol records through Northstar's normal script logger.
global function PVBridge_Init

struct
{
	bool started = false
	bool chargeActive = false
	string chargeWeapon = ""
	float nextChargeSample = 0.0
} file

void function PVBridge_Init()
{
	if ( file.started )
		return
	file.started = true
	thread PVBridge_Watch()
}

// Grouping controls initial recovery only; the bridge adapts to observed cadence.
string function PVBridge_Group( string name )
{
	array<string> heavy = [ "mp_weapon_shotgun", "mp_weapon_mastiff",
		"mp_weapon_shotgun_pistol", "mp_weapon_shotgun_doublebarrel",
		"mp_weapon_sniper", "mp_weapon_doubletake", "mp_weapon_dmr",
		"mp_weapon_epg", "mp_weapon_smr", "mp_weapon_softball",
		"mp_weapon_pulse_lmg", "mp_weapon_rocket_launcher", "mp_weapon_arc_launcher",
		"mp_weapon_mgl", "mp_weapon_defender" ]
	foreach ( string item in heavy )
		if ( name == item )
			return "shotgun"
	array<string> light = [ "mp_weapon_semipistol", "mp_weapon_autopistol",
		"mp_weapon_wingman", "mp_weapon_wingman_n", "mp_weapon_smart_pistol",
		"mp_weapon_gibber_pistol", "mp_weapon_alternator_smg", "mp_weapon_car",
		"mp_weapon_r97", "mp_weapon_hemlok_smg", "sp_weapon_arc_tool" ]
	foreach ( string item in light )
		if ( name == item )
			return "pistol"
	return "rifle"
}

bool function PVBridge_RocketOrdnance( string name )
{
	return name == "mp_titanweapon_shoulder_rockets"
		|| name == "mp_titanweapon_homing_rockets"
		|| name == "mp_titanweapon_salvo_rockets"
		|| name == "mp_titanweapon_dumbfire_rockets"
}

void function PVBridge_Emit( string group, entity weapon )
{
	print( "[PVBRIDGE/1] shot=" + group + " weapon=" + weapon.GetWeaponClassName() )
}

// Only known primary charge weapons: shields/heat weapons use this API too,
// but must not produce a charging effect merely because they hold a resource.
void function PVBridge_StopCharge()
{
	if ( file.chargeActive )
		print( "[PVBRIDGE/1] charge=0 weapon=" + file.chargeWeapon )
	file.chargeActive = false
	file.nextChargeSample = 0.0
}

void function PVBridge_UpdateCharge( entity player, entity weapon, bool fired )
{
	string name = weapon.GetWeaponClassName()
	bool held = false
	if ( name == "mp_titanweapon_sniper" )
		held = weapon.IsWeaponInAds()
	else if ( name == "mp_weapon_defender" )
		held = player.IsInputCommandHeld( IN_ATTACK )
	else
	{
		PVBridge_StopCharge()
		return
	}
	float fraction = weapon.GetWeaponChargeFraction()
	if ( fired || !held || fraction <= 0.0 )
	{
		PVBridge_StopCharge()
		return
	}
	int percent = int( fraction * 100.0 )
	if ( percent < 1 )
		percent = 1
	if ( percent > 100 )
		percent = 100
	if ( !file.chargeActive || Time() >= file.nextChargeSample )
	{
		print( "[PVBRIDGE/1] charge=" + percent + " weapon=" + name )
		file.chargeWeapon = name
		file.chargeActive = true
		file.nextChargeSample = Time() + 0.05
	}
}

void function PVBridge_Watch()
{
	entity owner = null
	entity primary = null
	entity secondary = null
	table<entity, int> clips = {}
	table<entity, float> attacks = {}
	table<entity, float> charges = {}
	int secondaryAmmo = -1
	float secondaryDelay = 0.0
	float lastRocket = -100.0
	float lastFrame = -1.0
	float nextHeartbeat = 0.0
	while ( true )
	{
		WaitFrame()
		float now = Time()
		if ( now >= nextHeartbeat )
		{
			print( "[PVBRIDGE/1] ready version=0.3.3" )
			nextHeartbeat = now + 5.0
		}
		entity currentOwner = GetLocalClientPlayer()
		// Rebaseline after respawn, player replacement or a long simulation gap.
		bool reset = currentOwner != owner || lastFrame < 0.0 || now < lastFrame || now - lastFrame > 0.25
		lastFrame = now
		owner = currentOwner
		if ( !IsValid( owner ) || !IsAlive( owner ) )
		{
			PVBridge_StopCharge()
			primary = null
			clips.clear()
			attacks.clear()
			charges.clear()
			secondary = null
			continue
		}
		if ( reset )
		{
			PVBridge_StopCharge()
			primary = null
			clips.clear()
			attacks.clear()
			charges.clear()
			secondary = null
		}
		entity active = owner.GetActiveWeapon()
		if ( IsValid( active ) )
		{
			if ( active != primary )
				PVBridge_StopCharge()
			int ammo = active.GetWeaponPrimaryClipCount()
			// One observed discharge per frame; ammo consumption is not a shot count.
			bool fired = false
			if ( active in clips )
			{
				fired = clips[ active ] > 0 && ammo >= 0 && ammo < clips[ active ]
				// Charge discharge can use an energy/cooldown transition without a clip drop.
				// A charge disappearing alone is cancellation, never a shot.
				string name = active.GetWeaponClassName()
				bool chargeWeapon = name == "mp_weapon_defender" || name == "mp_titanweapon_sniper"
				if ( !fired && chargeWeapon && owner.IsInputCommandHeld( IN_ATTACK ) )
				{
					float attack = active.GetNextAttackAllowedTime()
					fired = charges[ active ] > 0.0 && active.GetWeaponChargeFraction() <= 0.0
						&& attack > attacks[ active ] + 0.001 && attack > now
				}
			}
			if ( fired )
				PVBridge_Emit( owner.IsTitan() ? "titan" : PVBridge_Group( active.GetWeaponClassName() ), active )
			PVBridge_UpdateCharge( owner, active, fired )
			primary = active
		}
		else
		{
			PVBridge_StopCharge()
			primary = null
		}


		// Snapshot the whole inventory, including holstered guns. A switch and first
		// discharge in the same frame still compares against that gun's prior clip.
		table<entity, int> nextClips = {}
		table<entity, float> nextAttacks = {}
		table<entity, float> nextCharges = {}
		array<entity> inventory = owner.GetMainWeapons()
		if ( IsValid( active ) && !inventory.contains( active ) )
			inventory.append( active )
		foreach ( entity weapon in inventory )
		{
			if ( !IsValid( weapon ) )
				continue
			nextClips[ weapon ] <- weapon.GetWeaponPrimaryClipCount()
			nextAttacks[ weapon ] <- weapon.GetNextAttackAllowedTime()
			nextCharges[ weapon ] <- weapon.GetWeaponChargeFraction()
		}
		clips = nextClips
		attacks = nextAttacks
		charges = nextCharges

		if ( !owner.IsTitan() )
		{
			secondary = null
			continue
		}
		entity ordnance = owner.GetOffhandWeapon( OFFHAND_ORDNANCE )
		if ( !IsValid( ordnance ) || ordnance == active || !PVBridge_RocketOrdnance( ordnance.GetWeaponClassName() ) )
		{
			secondary = null
			continue
		}
		int ammo = ordnance.GetWeaponPrimaryClipCount()
		float delay = ordnance.GetNextAttackAllowedTime() - now
		if ( delay < 0.0 )
			delay = 0.0
		if ( ordnance != secondary )
			lastRocket = -100.0
		else
		{
			bool spent = secondaryAmmo > 0 && ammo >= 0 && ammo < secondaryAmmo
			bool cooldownBegan = ammo == secondaryAmmo && secondaryDelay < 0.05 && delay > 0.2
			if ( spent || cooldownBegan )
			{
				if ( now - lastRocket > 3.0 )
					PVBridge_Emit( "titan_rockets", ordnance )
				lastRocket = now
			}
		}
		secondary = ordnance
		secondaryAmmo = ammo
		secondaryDelay = delay
	}
}
