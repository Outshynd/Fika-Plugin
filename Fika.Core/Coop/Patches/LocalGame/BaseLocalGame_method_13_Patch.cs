﻿using EFT;
using Fika.Core.Coop.GameMode;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace Fika.Core.Coop.Patches.LocalGame
{
	/// <summary>
	/// Used to prevent players from getting everyone elses BTR items
	/// </summary>
	public class BaseLocalGame_method_13_Patch : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return typeof(BaseLocalGame<EftGamePlayerOwner>).GetMethod(nameof(BaseLocalGame<EftGamePlayerOwner>.method_13));
		}

		[PatchPrefix]
		public static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance, ref Dictionary<string, GClass1267[]> __result)
		{
			if (__instance is CoopGame coopGame)
			{
				__result = coopGame.GetOwnBTRTransfers(coopGame.ProfileId);
				return false;
			}
			return true;
		}
	}
}