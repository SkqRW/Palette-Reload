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

[BepInPlugin("seko.hotpalette", "Palette Hot Reload", "0.4")]
sealed class ModPlugin : BaseUnityPlugin
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
    }

    static bool changedThisFrame = false;

    private void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self)
    {
        orig(self);

        if (!self.game.devToolsActive){
            // A del tool thing a guess
            return;
        }

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
