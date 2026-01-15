using BepInEx;
using BepInEx.Logging;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System;
using System.Linq;

#pragma warning disable CS0618
[assembly: System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace HotPalette;

[BepInPlugin("seko.hotpalette", "Palette Hot Reload", "0.4")]
sealed class ModPlugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;
    bool isInit;


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
        On.DevInterface.TerrainPanel.GetPalettes += DevInterface_TerrainPanel_GetPalettes;
    }

    private List<string> DevInterface_TerrainPanel_GetPalettes(On.DevInterface.TerrainPanel.orig_GetPalettes orig, DevInterface.TerrainPanel self, bool fade)
    {
        //Just only accept .png files in the dev tools view
        List<string> list = (from name in AssetManager.ListDirectory("terrainpalettes", false, false, false)
                            where name.EndsWith(".png")
                            select name).Select(new Func<string, string>(Path.GetFileNameWithoutExtension)).ToList<string>();
        list.Insert(0, "NO PALETTE");
        if (!fade)
        {
            list.Insert(0, "INHERIT");
        }
        return list;
    }

    static bool changedThisFrame = true;

    
    FileSystemWatcher watcherA;
    FileSystemWatcher watcherB;
    FileSystemWatcher watcherTerrainA;

    static readonly HashSet<int> pendingReloads = new();
    static readonly HashSet<string> pendingTerrainReloads = new();

    int lastA = 0;
    int lastB = 0;
    string lastTerrainA = "";


    private void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self)
    {
        orig(self);

        if (!self.game.devToolsActive){
            // A del tool thing a guess
            return;
        }
        var sw = Stopwatch.StartNew();

        UpdateWatchers(self.paletteA, self.paletteB, self.terrainPalette.MainPaletteName);
        TryReloadPalette(self, self.paletteA, ref self.fadeTexA);
        TryReloadPalette(self, self.paletteB, ref self.fadeTexB);
        TryReloadTerrainPalette(self, self.terrainPalette.MainPaletteName, self.terrainPalette.FadePaletteName);

        if (changedThisFrame)
        {
            self.ApplyFade();

            self.terrainPalette = new TerrainPalette(self.terrainPalette.MainPaletteName, self.terrainPalette.FadePaletteName);
            self.ReloadTerrainPalette();

            self.terrainPalette = new TerrainPalette(self.terrainPalette.MainPaletteName, self.terrainPalette.FadePaletteName);
            self.ReloadTerrainPalette();

            changedThisFrame = false;
        }

        sw.Stop();
        UnityEngine.Debug.Log($"[HotPalette] Frame reload check: {sw.Elapsed.TotalMilliseconds} ms");
    }

    

    void UpdateWatchers(int palA, int palB, string terrainA)
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

        if (terrainA != lastTerrainA)
        {
            lastTerrainA = terrainA;
            ConfigureTerrainWatcher(ref watcherTerrainA, terrainA);
        }
    }

    private void ConfigureTerrainWatcher(ref FileSystemWatcher watcherTerrainA, string terrainA)
    {
        // Destroy previous watcher
        if (watcherTerrainA != null)
        {
            watcherTerrainA.EnableRaisingEvents = false;
            watcherTerrainA.Dispose();
            watcherTerrainA = null;
        }

        string path = AssetManager.ResolveFilePath($"terrainpalettes/{terrainA}.png", true, false);
        if (!File.Exists(path))
        {
            Logger.LogWarning($"[HotPalette] Terrain palette {terrainA} not found.");
            return;
        }

        watcherTerrainA = new FileSystemWatcher();
        watcherTerrainA.Path = Path.GetDirectoryName(path);
        watcherTerrainA.Filter = Path.GetFileName(path);
        watcherTerrainA.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
        watcherTerrainA.Changed += (s, e) => pendingTerrainReloads.Add(terrainA);
        watcherTerrainA.EnableRaisingEvents = true;

        //Logger.LogMessage($"[HotPalette] Observing terrain palette → {path}");
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
        watcher.Changed += (s, e) => pendingReloads.Add(id);
        watcher.EnableRaisingEvents = true;

        //Logger.LogMessage($"[HotPalette] Observing palette {id} → {path}");
    }

    private void TryReloadTerrainPalette(RoomCamera cam, string mainPaletteName, string fadePaletteName)
    {
        if (pendingTerrainReloads.Remove(mainPaletteName))
        {
            UnityEngine.Debug.Log($"[HotPalette] Reloading terrain palette {mainPaletteName}");
            changedThisFrame = true;
        }
    }

    private void TryReloadPalette(RoomCamera cam, int palID, ref Texture2D fadeTex)
    {
        if (pendingReloads.Remove(palID)) 
        {
            LoadPalette(cam, palID, ref fadeTex);
            changedThisFrame = true;
        }
    }

    private void LoadPalette(RoomCamera cam, int pal, ref Texture2D texture)
    {
        if (texture != null)
			UnityEngine.Object.Destroy(texture);
		
		texture = new Texture2D(32, 16, TextureFormat.ARGB32, false);

        string palettePath = AssetManager.ResolveFilePath(
            $"Palettes{Path.DirectorySeparatorChar}palette{pal}.png", 
            true, 
            false
        );

		try
		{
			AssetManager.SafeWWWLoadTexture(ref texture, $"file:///{palettePath}", false, true);
		}
		catch (FileLoadException)
		{
			palettePath = AssetManager.ResolveFilePath($"Palettes{Path.DirectorySeparatorChar}palette-1.png", true, false);
			AssetManager.SafeWWWLoadTexture(ref texture, $"file:///{palettePath}", false, true);
		}

		var (colorA, colorB) = (cam.room != null)
                                ? (cam.room.roomSettings.EffectColorA, cam.room.roomSettings.EffectColorB)
                                : (-1, -1);

        cam.ApplyEffectColorsToPaletteTexture(ref texture, colorA, colorB);
		texture.Apply(false);
    }
}
