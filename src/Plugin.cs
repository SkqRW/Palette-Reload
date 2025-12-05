using BepInEx;
using BepInEx.Logging;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

#pragma warning disable CS0618
[assembly: System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace HotPalette;

[BepInPlugin("seko.hotpalette", "Palette Hot Reload", "0.3.0")]
sealed class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;
    bool isInit;

    FileSystemWatcher watcherA;
    FileSystemWatcher watcherB;

    static readonly Dictionary<int, byte[]> pendingReloads = new();

    int lastA = -1;
    int lastB = -1;

    public void OnEnable()
    {
        Logger = base.Logger;
        On.RainWorld.OnModsInit += OnModsInit;
    }

    private void OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);
        if (isInit) return;
        isInit = true;

        Logger.LogMessage("[HotPalette] Loaded!");

        On.RoomCamera.Update += RoomCamera_Update;
    }

    static bool changedThisFrame = false;

    private void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self)
    {
        orig(self);

        UpdateWatchers(self.paletteA, self.paletteB);

        var sw = Stopwatch.StartNew();
        TryReloadPalette(self, self.paletteA, ref self.fadeTexA);
        TryReloadPalette(self, self.paletteB, ref self.fadeTexB);

        if (changedThisFrame)
        {
            self.ApplyFade();
            changedThisFrame = false;
        }

        sw.Stop();
        UnityEngine.Debug.Log($"[HotPalette] Frame reload check: {sw.Elapsed.TotalMilliseconds} ms");
    }

    void UpdateWatchers(int palA, int palB)
    {
        if (palA != lastA)
        {
            lastA = palA;
            ConfigureWatcher(ref watcherA, palA);
        }

        if (palB != lastB)
        {
            lastB = palB;
            ConfigureWatcher(ref watcherB, palB);
        }
    }

    void ConfigureWatcher(ref FileSystemWatcher watcher, int id)
    {
        // Destroy previous watcher
        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            watcher = null;
        }

        if (id < 0) return;

        string path = FindPaletteFile(id);
        if (!File.Exists(path))
        {
            Logger.LogWarning($"[HotPalette] Palette {id} not found.");
            return;
        }

        watcher = new FileSystemWatcher();
        watcher.Path = Path.GetDirectoryName(path);
        watcher.Filter = Path.GetFileName(path);
        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
        watcher.Changed += (s, e) => OnPaletteChanged(id, e.FullPath);
        watcher.EnableRaisingEvents = true;

        //Logger.LogMessage($"[HotPalette] Observing palette {id} → {path}");
    }

    void OnPaletteChanged(int id, string fullPath)
    {
        /*
                ()____()
	            | O - O)      WAWA
	            |      |
            ____/      )

        */
        try
        {
            byte[] data = File.ReadAllBytes(fullPath);
            pendingReloads[id] = data;
        }
        catch { 
            Logger.LogWarning($"[HotPalette] Could not read modified palette {id}.");
        }
    }

    string FindPaletteFile(int id)
    {
        string path = $"Palettes/palette{id}.png";
        return AssetManager.ResolveFilePath(path);
    }

    private void TryReloadPalette(RoomCamera cam, int palID, ref Texture2D fadeTex)
    {
        if (!pendingReloads.TryGetValue(palID, out byte[] newFileData))
            return;

        if (TextureDifferent(fadeTex, newFileData))
        {
            cam.LoadPalette(palID, ref fadeTex);
            changedThisFrame = true;
        }

        pendingReloads.Remove(palID);
    }

    private bool TextureDifferent(Texture2D tex, byte[] fileData)
    {
        if (tex == null) return true;

        byte[] texBytes = tex.GetRawTextureData();

        // Just in case, unity...
        if (fileData.Length != texBytes.Length)
            return true;

        for (int i = 0; i < texBytes.Length; i++)
            if (texBytes[i] != fileData[i])
                return true;

        return false;
    }
}
