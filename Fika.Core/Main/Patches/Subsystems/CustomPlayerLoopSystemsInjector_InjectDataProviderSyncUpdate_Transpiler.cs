﻿using CustomPlayerLoopSystem;
using Fika.Core.Patching;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Fika.Core.Main.Patches.Subsystems;

/// <summary>
/// Prevents logic that is never used from being injected into the <see cref="UnityEngine.LowLevel.PlayerLoop"/>
/// </summary>
internal class CustomPlayerLoopSystemsInjector_InjectDataProviderSyncUpdate_Transpiler : FikaPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(CustomPlayerLoopSystemsInjector).GetMethod(nameof(CustomPlayerLoopSystemsInjector.InjectDataProviderSyncUpdate));
    }

    [PatchTranspiler]
    public static IEnumerable<CodeInstruction> Transpile()
    {
        yield return new(OpCodes.Ret);
    }
}
