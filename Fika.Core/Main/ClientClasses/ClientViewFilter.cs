﻿using EFT;
using Fika.Core.Main.Components;
using System.Collections.Generic;

namespace Fika.Core.Main.ClientClasses
{
    public sealed class ClientViewFilter : ViewFilter
    {
        public override HashSet<EBodyModelPart> AllowedParts
        {
            get
            {
                return [EBodyModelPart.Body, EBodyModelPart.Feet, EBodyModelPart.Head, EBodyModelPart.Hands];
            }
        }
    }
}
