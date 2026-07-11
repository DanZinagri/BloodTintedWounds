using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace BloodTintedWounds
{
    // See Defs/WoundTextureOverrides.xml for commented-out examples.
    public class BloodWoundTexturesDef : Def
    {
        public GeneDef gene;

        public HediffDef hediff;

        public bool tintWithBloodColor;

        public Color? customTintColor;

        public List<FleshTypeDef.Wound> wounds;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (gene == null)
            {
                yield return "BloodWoundTexturesDef has no gene set; it will be ignored.";
            }
            if (wounds.NullOrEmpty())
            {
                yield return "BloodWoundTexturesDef has no wounds defined; it will be ignored.";
            }
        }
    }
}
