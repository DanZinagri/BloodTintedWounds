using RimWorld;
using UnityEngine;
using Verse;

namespace BloodTintedWounds
{
    // Settings menu: no persisted options, just the cache regeneration button. Staleness of
    // the grayscale cache is the user's call (texture packs can change art without changing
    // dimensions, so there is no reliable automatic check).
    public class BloodTintedWoundsMod : Mod
    {
        public BloodTintedWoundsMod(ModContentPack pack) : base(pack)
        {
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);

            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("Cached grayscale wound textures folder:\n" + GrayscaleTextureCache.CacheDir);
            listing.Gap();

            if (listing.ButtonText("Regenerate texture cache"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "This deletes every cached grayscale wound texture and rebuilds them from the currently loaded mods right now. Do this after adding, removing or updating wound texture mods. Hand-made overrides (BloodWoundTexturesDef) are unaffected. Continue?",
                    delegate
                    {
                        int cleared = FleshTypeGenerator.RegenerateAll();
                        Messages.Message(
                            $"Cleared {cleared} cached wound texture(s) and remade tinted wounds.",
                            MessageTypeDefOf.TaskCompletion,
                            historical: false);
                    },
                    destructive: true));
            }

            listing.End();
        }

        public override string SettingsCategory()
        {
            return "Blood-Tinted Wounds";
        }
    }
}
