﻿using EFT;
using System.Collections.Generic;

namespace Fika.Core.Main.Components;

public abstract class ViewFilter : IViewFilter
{
    public abstract HashSet<EBodyModelPart> AllowedParts { get; }

    public GClass2197 FilterCustomization(GClass2197 customization)
    {
        GClass2197 value = new(customization);
        for (int i = 0; i < GClass866<EBodyModelPart>.Values.Count; i++)
        {
            EBodyModelPart bodyPart = GClass866<EBodyModelPart>.Values[i];
            if (!AllowedParts.Contains(bodyPart))
            {
                value.Remove(bodyPart);
            }
        }
        return value;
    }
}
