using System;
using Verse;

namespace BloodTintedWounds
{
    [StaticConstructorOnStartup]
    public static class Startup
    {
        static Startup()
        {
            try
            {
                FleshTypeGenerator.Run();
            }
            catch (Exception e)
            {
                Log.Error("[Blood-Tinted Wounds] Failed to create tinted wound sets: " + e);
            }
        }
    }
}
