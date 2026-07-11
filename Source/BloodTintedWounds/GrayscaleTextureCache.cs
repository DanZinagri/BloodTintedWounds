using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace BloodTintedWounds
{
    // Desaturated wound textures, cached to disk so the RenderTexture round-trip only ever happens once per texture instead of on every game launch (same pattern as PA_DynamicSeedIcons).
    // Lookup order per texture path:
    //  1. In-memory/override.
    //  2. A PNG cached in Config/DZ_BloodWoundTextures from a previous run.
    //  3. Generate from the source texture, then cache the PNG for next time.
    // Staleness is the user's call: the "Regenerate texture cache" button in mod settings (or deleting the DZ_BloodWoundTextures folder) forces everything to rebuild.
    public static class GrayscaleTextureCache
    {
        private static readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>();

        public static string CacheDir => Path.Combine(GenFilePaths.ConfigFolderPath, "DZ_BloodWoundTextures");

        public static Texture2D Get(string texPath, Texture2D source)
        {
            if (loaded.TryGetValue(texPath, out Texture2D gray))
            {
                return gray;
            }
            gray = TryLoadFromDisk(texPath, source) ?? GenerateAndCache(texPath, source);
            loaded.Add(texPath, gray);
            return gray;
        }

        // Deletes the disk cache and forgets this session's textures so the next Get()
        // regenerates from the currently loaded art. Returns how many files were deleted.
        public static int ClearAll()
        {
            loaded.Clear();
            if (!Directory.Exists(CacheDir))
            {
                return 0;
            }
            string[] files = Directory.GetFiles(CacheDir, "*.png");
            foreach (string file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Log.Warning($"[Blood-Tinted Wounds] Failed to delete cached texture '{file}': {e}");
                }
            }
            return files.Length;
        }

        private static string CacheFile(string texPath)
        {
            return Path.Combine(CacheDir, texPath.Replace('/', '_').Replace('\\', '_') + ".png");
        }

        private static Texture2D TryLoadFromDisk(string texPath, Texture2D source)
        {
            string file = CacheFile(texPath);
            if (!File.Exists(file))
            {
                return null;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.filterMode = source.filterMode;
                tex.wrapMode = source.wrapMode;
                tex.name = source.name + "_GrayCached";
                tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
                return tex;
            }
            catch (Exception e)
            {
                Log.Warning($"[Blood-Tinted Wounds] Failed reading cached grayscale for '{texPath}': {e}");
                return null;
            }
        }

        private static Texture2D GenerateAndCache(string texPath, Texture2D source)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D gray = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: true);
            gray.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] pixels = gray.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                byte value = p.r > p.g ? (p.r > p.b ? p.r : p.b) : (p.g > p.b ? p.g : p.b);
                pixels[i] = new Color32(value, value, value, p.a);
            }
            gray.SetPixels32(pixels);
            gray.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            SaveToDisk(texPath, gray);
            gray.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            gray.filterMode = source.filterMode;
            gray.wrapMode = source.wrapMode;
            gray.name = source.name + "_Gray";
            return gray;
        }

        private static void SaveToDisk(string texPath, Texture2D gray)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllBytes(CacheFile(texPath), gray.EncodeToPNG());
            }
            catch (Exception e)
            {
                Log.Warning($"[Blood-Tinted Wounds] Failed to cache grayscale for '{texPath}': {e}");
            }
        }
    }
}
