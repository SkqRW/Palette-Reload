using BepInEx;
using BepInEx.Logging;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System;

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

    static readonly HashSet<int> pendingReloads = new();

    int lastA = 0;
    int lastB = 0;

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
        On.AssetManager.ResolveFilePath_string += AssetManager_ResolveFilePath;
    }

    private string AssetManager_ResolveFilePath(On.AssetManager.orig_ResolveFilePath_string orig, string path)
    {
        // Overriding this function to don't search in the merge folder
        // Load palette call this function, and the default is false, false
        // But the user modify palette in his mod, not in merge
        // So for dev, we will use the mods paths
        return AssetManager.ResolveFilePath(path, true, false);
    }

    static bool changedThisFrame = false;

    private void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self)
    {
        orig(self);

        UpdateWatchers(self.paletteA, self.paletteB);

        //var sw = Stopwatch.StartNew();
        TryReloadPalette(self, self.paletteA, ref self.fadeTexA);
        TryReloadPalette(self, self.paletteB, ref self.fadeTexB);

        if (changedThisFrame)
        {
            self.ApplyFade();
            changedThisFrame = false;
        }

        //sw.Stop();
        //UnityEngine.Debug.Log($"[HotPalette] Frame reload check: {sw.Elapsed.TotalMilliseconds} ms");
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

        string path = AssetManager.ResolveFilePath($"Palettes/palette{id}.png", true, false);
        if (!File.Exists(path))
        {
            Logger.LogWarning($"[HotPalette] Palette {id} not found.");
            return;
        }

        watcher = new FileSystemWatcher();
        watcher.Path = Path.GetDirectoryName(path);
        watcher.Filter = Path.GetFileName(path);
        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
        watcher.Changed += (s, e) => OnPaletteChanged(id);
        watcher.EnableRaisingEvents = true;

        //Logger.LogMessage($"[HotPalette] Observing palette {id} → {path}");
    }

    void OnPaletteChanged(int id)
    {
        /*
                ()____()
	            | O - O)      WAWA
	            |      |
            ____/      )

        */
        pendingReloads.Add(id);
    }


    private void TryReloadPalette(RoomCamera cam, int palID, ref Texture2D fadeTex)
    {
        if (pendingReloads.Remove(palID))  // Remove devuelve true si existía
        {
            cam.LoadPalette(palID, ref fadeTex);
            changedThisFrame = true;
        }
    }
}
