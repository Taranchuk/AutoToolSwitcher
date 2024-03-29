﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace AutoToolSwitcher
{
    public static class WeaponSearchUtility
    {
		public static bool CanLookForWeapon(this Pawn pawn)
        {
			if (pawn.Map == null || pawn.equipment == null || pawn.Faction != Faction.OfPlayer || pawn.IsQuestLodger()
				|| !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || pawn.WorkTagIsDisabled(WorkTags.Violent))
			{
				return false;
			}
			if (ModCompatUtility.combatExtendedLoaded)
            {
				var primary = pawn.equipment.Primary;
				if (ModCompatUtility.HasActiveInCELoadout(pawn, primary, out bool hasActiveWeaponsInLoadout))
				{
					return false;
				}
				if (hasActiveWeaponsInLoadout)
                {
					return false;
                }
			}
			return true;
		}
		public static ThingWithComps PickBestWeapon(Pawn pawn, Func<Pawn, ThingWithComps, bool> validator)
        {
			ThingWithComps thing = null;
			float maxValue = 0f;
			List<ThingWithComps> weapons = pawn.inventory.innerContainer.InnerListForReading.OfType<ThingWithComps>().Where(x => validator(pawn, x)).ToList();
			for (int j = 0; j < weapons.Count; j++)
			{
				ThingWithComps weapon = weapons[j];
				if (!weapon.IsBurning() && (!CompBiocodable.IsBiocoded(weapon) || CompBiocodable.IsBiocodedFor(weapon, pawn))
						&& EquipmentUtility.CanEquip(weapon, pawn) && pawn.CanReserveAndReach(weapon, PathEndMode.OnCell, pawn.NormalMaxDanger()))
				{
					var policy = pawn.GetCurrentToolPolicy();
					if (policy != null)
                    {
						if (!policy[weapon.def].equipAsWeapon || weapon.TryGetQuality(out var qc) && policy.minQuality > qc)
                        {
							continue;
						}
					}
					float weaponScore = WeaponScoreGain(weapon);
					if (!(weaponScore < 0.05f) && !(weaponScore < maxValue))
					{
						thing = weapon;
						maxValue = weaponScore;
					}
				}
			}
			return thing;
		}
		public static bool MainWeaponValidator(Thing t, Pawn pawn, bool isBrawler, ToolPolicy policy)
		{
			if (!t.def.IsWeapon)
			{
				//Log.Message("False 1");
				return false;
			}
			if (t.IsForbidden(pawn))
			{
				//Log.Message("False 2");

				return false;
			}
			if (isBrawler && t.def.IsRangedWeapon)
			{
				//Log.Message("False 3");

				return false;
			}
			if (t.def.weaponTags != null && t.def.weaponTags.Where(x => x.ToLower().Contains("grenade")).Any())
			{
				//Log.Message("False 4");

				return false;
			}
			if (t.def.IsRangedWeapon && t.def.Verbs.Where(x => x.verbClass == typeof(Verb_ShootOneUse)).Any())
			{
				//Log.Message("False 5");

				return false;
			}

			if (ToolSearchUtility.fireExtinguishers.Contains(t.def))
			{
				//Log.Message("False 6");

				return false;
			}
			if (ModCompatUtility.combatExtendedLoaded && !ModCompatUtility.IsUsableForCE(pawn, t))
			{
				//Log.Message("False 7");

				return false;
			}
			if (!ToolSearchUtility.baseEquipmentValidator(pawn, t, policy))
			{
				//Log.Message("False 8");

				return false;
			}
			return true;
		}

		public static bool SecondaryWeaponValidator(Pawn pawn, ToolPolicy policy, Thing t)
		{
			if (policy != null && !policy[t.def].takeAsSecondary)
			{
				return false;
			}
			return true;
		}

		public static bool PreferabilityValidator(bool preferRanged, Thing t)
		{
			if (preferRanged && t.def.IsMeleeWeapon)
			{
				return false;
			}
			if (!preferRanged && t.def.IsRangedWeapon)
			{
				return false;
			}
			return true;
		}
		public static Thing PickBestWeaponFor(Pawn pawn, out Thing secondaryWeapon)
		{
			var primary = pawn.equipment?.Primary;
			bool isBrawler = pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) ?? false;
			bool preferRanged = !isBrawler && (!primary?.def.IsMeleeWeapon ?? false || primary == null);
			secondaryWeapon = null;
			var policy = pawn.GetCurrentToolPolicy();
			Thing thing = null;
			float maxValue = 0f;
			List<Thing> list = new List<Thing>();
			list.AddRange(pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon));
			list.AddRange(pawn.inventory.innerContainer.Where(x => x.def.IsWeapon).ToList());
			if (primary != null)
			{
				list.Add(primary);
			}
			
			Dictionary<Thing, float> weaponsByScores = new Dictionary<Thing, float>();
			for (int j = 0; j < list.Count; j++)
			{
				Thing weapon = list[j];
				var policyEntry = policy[weapon.def];
				//Log.Message("Checking weapon: " + weapon + " - " + (policyEntry.equipAsWeapon || policyEntry.takeAsSecondary) 
				//	+ " - " + MainWeaponValidator(weapon, pawn, isBrawler, policy) + " policy: " + policy.label);
				if ((policyEntry.equipAsWeapon || policyEntry.takeAsSecondary) && MainWeaponValidator(weapon, pawn, isBrawler, policy)
					&& !weapon.IsBurning() && (!CompBiocodable.IsBiocoded(weapon) || CompBiocodable.IsBiocodedFor(weapon, pawn))
					&& EquipmentUtility.CanEquip(weapon, pawn))
				{
					float weaponScore = WeaponScoreGain(weapon);
					//Log.Message("Can look into " + weapon + " - score: " + weaponScore);
					//Log.Message("policyEntry.equipAsWeapon: " + policyEntry.equipAsWeapon + " - !(weaponScore < maxValue): " + !(weaponScore < maxValue) + " - PreferabilityValidator(preferRanged, weapon): " + PreferabilityValidator(preferRanged, weapon));
					weaponsByScores[weapon] = weaponScore;
					if (policyEntry.equipAsWeapon && !(weaponScore < maxValue) && PreferabilityValidator(preferRanged, weapon))
					{
						thing = weapon;
						maxValue = weaponScore;
					}
				}
			}
			
			if (thing != null)
			{
				secondaryWeapon = weaponsByScores.OrderByDescending(x => x.Value).FirstOrDefault(x => SecondaryWeaponValidator(pawn, policy, x.Key) && x.Key.def.IsRangedWeapon != thing.def.IsRangedWeapon).Key;
			}
			return thing;
		}

		private static Dictionary<Thing, float> cachedResults = new Dictionary<Thing, float>();
		public static float WeaponScoreGain(this Thing weapon)
		{
			if (!cachedResults.TryGetValue(weapon, out var result))
            {
				cachedResults[weapon] = result = WeaponScoreGainInt(weapon);
			}
			return result;
		}
		private static float WeaponScoreGainInt(Thing weapon)
		{
			if (weapon.def.IsRangedWeapon)
			{
				var verbProperties = weapon.def.Verbs.First(x => x.range > 0);
				double num = (verbProperties.defaultProjectile.projectile.GetDamageAmount(weapon, null) * (float)verbProperties.burstShotCount);
				float num2 = (StatExtension.GetStatValue(weapon, StatDefOf.RangedWeapon_Cooldown, true) + verbProperties.warmupTime) * 60f;
				float num3 = (verbProperties.burstShotCount * verbProperties.ticksBetweenBurstShots);
				float num4 = (num2 + num3) / 60f;
				var dps = (float)Math.Round(num / num4, 2);
				var accuracy = StatExtension.GetStatValue(weapon, StatDefOf.AccuracyMedium, true) * 100f;
				return (float)Math.Round(dps * accuracy / 100f, 1);
			}
			else if (weapon.def.IsMeleeWeapon)
			{
				return StatExtension.GetStatValue(weapon, StatDefOf.MeleeWeapon_AverageDPS, true);
			}
			return 0f;
		}

		public static Job SearchForWeapon(Pawn pawn)
		{
			if (pawn.CanLookForWeapon())
			{
				var weapon = PickBestWeaponFor(pawn, out var secondaryWeapon);
				//Log.Message("Checking: " + weapon + " for " + pawn);
				if (weapon != null || secondaryWeapon != null)
				{
					if (weapon != null && weapon.def != pawn.equipment.Primary?.def)
					{
						if (pawn.inventory.innerContainer.Contains(weapon))
						{
							HarmonyPatches.EquipTool(pawn, weapon as ThingWithComps);
							return null;
						}
						else
						{
							return JobMaker.MakeJob(JobDefOf.Equip, weapon);
						}
					}
					else if (secondaryWeapon != null && !pawn.inventory.innerContainer.Any(x => x.def == secondaryWeapon.def))
					{
						if (pawn.equipment.Primary == secondaryWeapon)
						{
							if (secondaryWeapon.holdingOwner != null)
							{
								secondaryWeapon.holdingOwner.Remove(secondaryWeapon);
							}
							pawn.inventory.TryAddItemNotForSale(secondaryWeapon);
						}
						else
						{
							var job = JobMaker.MakeJob(JobDefOf.TakeInventory, secondaryWeapon);
							job.count = 1;
							return job;
						}
					}
				}
			}

			var toolPolicy = pawn.GetCurrentToolPolicy();
			if (toolPolicy != null)
			{
				var primary = pawn.equipment?.Primary;
				if (primary != null && primary.def.IsTool())
				{
					var curPolicy = toolPolicy[primary.def];
					if (!curPolicy.equipAsWeapon && !curPolicy.takeAsTool
						&& (!ModCompatUtility.combatExtendedLoaded || !ModCompatUtility.HasActiveInCELoadout(pawn, primary, out _)))
					{
						return HaulTool(pawn, primary);
					}
				}

				if (pawn.inventory != null)
				{
					var things = pawn.inventory.innerContainer.ToList();
					foreach (var thing in things)
					{
						if (thing.def.IsTool())
						{
							var curPolicy = toolPolicy[thing.def];
							if (!curPolicy.takeAsTool && !curPolicy.takeAsSecondary && (!ModCompatUtility.combatExtendedLoaded || !ModCompatUtility.HasActiveInCELoadout(pawn, thing, out _)))
							{
								return HaulTool(pawn, thing);
							}
						}
					}
				}
			}
			return null;
		}

		private static Job HaulTool(Pawn pawn, Thing thing)
		{
			Thing droppedThing;
			if (thing.holdingOwner.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, 1, out droppedThing))
			{
				if (droppedThing != null)
				{
					return HaulAIUtility.HaulToStorageJob(pawn, droppedThing);
				}
			}
			return null;
		}
	}
}
