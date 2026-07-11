using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using VEF.Genes;
using Verse;

namespace BloodTintedWounds
{
    // Startup-only generator. For every VEF blood-color gene it bakes a FleshTypeDef whose wound overlays are pre-tinted to the gene's blood color,
    // then writes it into the gene's customWoundsFromFleshtype slot.
    // Genes with BloodWoundTexturesDef overrides get their own def with the hand-supplied art swapped in; everyone else shares one def per color.
    public static class FleshTypeGenerator
    {
        private static readonly FieldInfo WoundsResolvedField = typeof(FleshTypeDef).GetField("woundsResolved", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BandagedWoundsResolvedField = typeof(FleshTypeDef).GetField("bandagedWoundsResolved", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HediffWoundsResolvedField = typeof(FleshTypeDef.HediffWound).GetField("woundsResolved", BindingFlags.NonPublic | BindingFlags.Instance);

        // Everything made at startup, remembered so the settings menu can rebake in place
        // (the defs are registered and referenced by gene extensions, so they must be reused).
        private class WoundSet
        {
            public FleshTypeDef def;
            public Color color;
            public List<BloodWoundTexturesDef> overrides;
        }

        private static readonly List<WoundSet> woundSets = new List<WoundSet>();
        private static FleshTypeDef sourceDef;

        public static void Run()
        {
            if (WoundsResolvedField == null || BandagedWoundsResolvedField == null || HediffWoundsResolvedField == null)
            {
                Log.Error("[Blood-Tinted Wounds] FleshTypeDef internals not found; something up with the game's wound system. Doing nothing.");
                return;
            }
            Dictionary<GeneDef, List<BloodWoundTexturesDef>> overridesByGene = CollectOverrides();
            FleshTypeDef source = sourceDef = FleshTypeDefOf.Normal;
            int geneCount = 0;
            foreach (GeneDef gene in DefDatabase<GeneDef>.AllDefsListForReading)
            {
                GeneExtension ext = gene.GetModExtension<GeneExtension>();
                if (ext?.customBloodThingDef == null) { continue; }
                Color color = ext.customBloodThingDef.graphicData?.color ?? Color.white;
                if (color.IndistinguishableFrom(Color.white)) { continue; }
                color.a = 1f;
                // One def per gene: overrides can never bleed onto another gene that happens to share a color.
                overridesByGene.TryGetValue(gene, out List<BloodWoundTexturesDef> overrides);
                FleshTypeDef defcolor = CacheColor(source, color, "DZ_BTW_Gene_" + gene.defName, overrides);
                // Overrides any re-shipped pre-colored wound art: that art is a pre-tint-era workaround, and replacing it lets texture packs apply to these pawns too.
                ext.customWoundsFromFleshtype = defcolor;
                geneCount++;
            }
            if (geneCount > 0)
            {
                Log.Message($"[Blood-Tinted Wounds] made tinted wound sets for {geneCount} blood gene(s).");
            }
        }

        private static Dictionary<GeneDef, List<BloodWoundTexturesDef>> CollectOverrides()
        {
            Dictionary<GeneDef, List<BloodWoundTexturesDef>> byGene = new Dictionary<GeneDef, List<BloodWoundTexturesDef>>();
            foreach (BloodWoundTexturesDef overrideDef in DefDatabase<BloodWoundTexturesDef>.AllDefsListForReading)
            {
                if (overrideDef.gene == null || overrideDef.wounds.NullOrEmpty())
                {
                    continue; // Already reported by ConfigErrors.
                }
                if (!byGene.TryGetValue(overrideDef.gene, out List<BloodWoundTexturesDef> list))
                {
                    list = new List<BloodWoundTexturesDef>();
                    byGene.Add(overrideDef.gene, list);
                }
                list.Add(overrideDef);
            }
            return byGene;
        }

        private static FleshTypeDef CacheColor(FleshTypeDef source, Color color, string defName, List<BloodWoundTexturesDef> overrides)
        {
            FleshTypeDef def = new FleshTypeDef
            {
                defName = defName,
                label = source.label ?? source.defName,
                generated = true,
                isOrganic = source.isOrganic,
                corpseCategory = source.corpseCategory,
                damageEffecter = source.damageEffecter,
                ateDirect = source.ateDirect,
                ateAsIngredient = source.ateAsIngredient,
                bandagedWounds = source.bandagedWounds,
            };
            FillResolved(def, source, color, overrides);
            DefGenerator.AddImpliedDef(def);
            woundSets.Add(new WoundSet { def = def, color = color, overrides = overrides });
            return def;
        }

        // Rebuilds a def's wound lists and pre-resolved material caches. Called at startup and again by the settings menu's cache regeneration.
        private static void FillResolved(FleshTypeDef def, FleshTypeDef source, Color color, List<BloodWoundTexturesDef> overrides)
        {
            // A generic override (no hediff) replaces the generic wounds wholesale.
            BloodWoundTexturesDef genericOverride = null;
            if (overrides != null)
            {
                foreach (BloodWoundTexturesDef o in overrides)
                {
                    if (o.hediff == null)
                    {
                        genericOverride = o;
                    }
                }
            }
            // Wound entries are shared with the source def (only their resolved materials differ, and those live in the private caches pre-filled below).
            List<FleshTypeDef.Wound> genericWounds = genericOverride?.wounds ?? source.genericWounds;
            def.genericWounds = genericWounds;
            WoundsResolvedField.SetValue(def, ResolveAll(genericWounds, EffectiveColor(genericOverride, color), ShouldTint(genericOverride)));
            BandagedWoundsResolvedField.SetValue(def, ResolveAll(source.bandagedWounds, color, tint: false));
            def.hediffWounds = null;
            if (source.hediffWounds != null)
            {
                def.hediffWounds = new List<FleshTypeDef.HediffWound>(source.hediffWounds.Count);
                foreach (FleshTypeDef.HediffWound hw in source.hediffWounds)
                {
                    // HediffWound owns a private resolved cache.
                    def.hediffWounds.Add(MakeHediffWound(hw.hediff, hw.onlyHumanlikes, hw.wounds, color, tint: true));
                }
            }
            if (overrides != null)
            {
                foreach (BloodWoundTexturesDef o in overrides)
                {
                    if (o.hediff == null)
                    {
                        continue;
                    }
                    if (def.hediffWounds == null)
                    {
                        def.hediffWounds = new List<FleshTypeDef.HediffWound>();
                    }
                    // Replace any entry inherited from the source, and sit at the front so the override wins the first-match scan in ChooseWoundOverlay.
                    def.hediffWounds.RemoveAll(existing => existing.hediff == o.hediff);
                    def.hediffWounds.Insert(0, MakeHediffWound(o.hediff, true, o.wounds, EffectiveColor(o, color), ShouldTint(o)));
                }
            }
        }

        // Overrides tint when asked to, or when they carry their own custom color; that color beats the gene's blood color and doesn't need to match it.
        private static bool ShouldTint(BloodWoundTexturesDef overrideDef)
        {
            return overrideDef == null || overrideDef.tintWithBloodColor || overrideDef.customTintColor.HasValue;
        }

        private static Color EffectiveColor(BloodWoundTexturesDef overrideDef, Color bloodColor)
        {
            if (overrideDef?.customTintColor == null)
            {
                return bloodColor;
            }
            Color custom = overrideDef.customTintColor.Value;
            custom.a = 1f;
            return custom;
        }

        // Settings-menu action: wipe the disk cache and remake every generated def from the currently loaded art. Returns how many cached files were deleted.
        public static int RegenerateAll()
        {
            int cleared = GrayscaleTextureCache.ClearAll();
            foreach (WoundSet set in woundSets)
            {
                FillResolved(set.def, sourceDef, set.color, set.overrides);
            }
            if (Current.ProgramState == ProgramState.Playing)
            {
                foreach (Map map in Find.Maps)
                {
                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    }
                }
            }
            return cleared;
        }

        private static FleshTypeDef.HediffWound MakeHediffWound(HediffDef hediff, bool onlyHumanlikes, List<FleshTypeDef.Wound> wounds, Color color, bool tint)
        {
            FleshTypeDef.HediffWound result = new FleshTypeDef.HediffWound
            {
                hediff = hediff,
                onlyHumanlikes = onlyHumanlikes,
                wounds = wounds,
            };
            HediffWoundsResolvedField.SetValue(result, ResolveAll(wounds, color, tint));
            return result;
        }

        private static List<FleshTypeDef.ResolvedWound> ResolveAll(List<FleshTypeDef.Wound> wounds, Color color, bool tint)
        {
            if (wounds == null)
            {
                return null;
            }
            List<FleshTypeDef.ResolvedWound> resolved = new List<FleshTypeDef.ResolvedWound>(wounds.Count);
            foreach (FleshTypeDef.Wound wound in wounds)
            {
                resolved.Add(ResolveOne(wound, color, tint));
            }
            return resolved;
        }

        private static FleshTypeDef.ResolvedWound ResolveOne(FleshTypeDef.Wound wound, Color color, bool tint)
        {
            // Skin-tinted overlays (scarification, healed missing-part scars) are skin, not blood; bandages aren't blood either. Both resolve exactly as vanilla would.
            if (!tint || wound.tintWithSkinColor)
            {
                return wound.Resolve();
            }
            return wound.texture != null
                ? new FleshTypeDef.ResolvedWound(wound, TintedMat(wound.texture, color))
                : new FleshTypeDef.ResolvedWound(wound,
                    TintedMat(wound.textureSouth, color),
                    TintedMat(wound.textureEast, color),
                    TintedMat(wound.textureNorth, color),
                    TintedMat(wound.textureWest, color),
                    wound.flipSouth, wound.flipEast, wound.flipNorth, wound.flipWest);
        }

        private static Material TintedMat(string texPath, Color color)
        {
            Texture2D source = ContentFinder<Texture2D>.Get(texPath, reportFailure: false);
            if (source == null)
            {
                return MaterialPool.MatFrom(texPath, ShaderDatabase.Wound, color);
            }
            Texture2D grayTex = GrayscaleTextureCache.Get(texPath, source);
            MaterialRequest req = default(MaterialRequest);
            req.mainTex = grayTex;
            req.shader = ShaderDatabase.Wound;
            req.color = color;
            return MaterialPool.MatFrom(req);
        }
    }
}
