using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace VerticalView;

public sealed class ModEntry : Mod
{
    private static ModEntry? instance;
    private static readonly MethodInfo DrawMethod = AccessTools.Method(typeof(Game1), "_draw");
    private static readonly MethodInfo AllocateLightmap = AccessTools.Method(typeof(Game1), "allocateLightmap");
    private static readonly FieldInfo LightmapField = AccessTools.Field(typeof(Game1), "_lightmap");
    private static readonly FieldInfo UiScreenField = AccessTools.Field(typeof(Game1), "_uiScreen");
    private static readonly FieldInfo GlTextureField = AccessTools.Field(typeof(Texture), "glTexture");
    private static readonly MethodInfo DayTimeUpdatePosition = AccessTools.Method(typeof(DayTimeMoneyBox), "updatePosition");

    // state shared with the HUD-lift patches while the second view is drawing
    private static bool renderingSecond;
    private static bool suppressMenuDraw;
    private static int effectiveUiWidth;
    private static int effectiveUiHeight;
    private static int hudLift;

    private ModConfig config = new ModConfig();
    private VerticalWindow? window;
    private readonly RenderTarget2D?[] targets = new RenderTarget2D?[2];
    private int flip;
    private RenderTarget2D? uiTarget;
    private RenderTarget2D? lightmap;
    private int renderWidth;
    private int renderHeight;
    private int frameCounter;
    private float dialogueUiScale;
    private bool secondFrameReady;
    private Type? lastLoggedMenuType;

    public override void Entry(IModHelper helper)
    {
        instance = this;
        config = helper.ReadConfig<ModConfig>();
        renderWidth = (int)(config.WindowWidth / config.Zoom);
        renderHeight = (int)(config.WindowHeight / config.Zoom);
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        Harmony harmony = new Harmony(ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "Draw", new[] { typeof(GameTime) }),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeDraw)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterDraw))
        );
        // lift the health/stamina bars and notifications above the toolbar in the second view only:
        // they anchor to the device viewport's title-safe bottom, so shrinking the viewport while
        // they draw moves them up; the toolbar anchors to uiViewport and needs the full viewport back
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "drawHUD"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeDrawHud)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterDrawHud))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(Toolbar), nameof(Toolbar.draw), new[] { typeof(SpriteBatch) }),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeToolbarDraw))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(HUDMessage), nameof(HUDMessage.draw)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeDrawHud)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterDrawHud))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "drawMouseCursor"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(SkipInSecondView))
        );
        // menus are drawn in a dedicated pass for the mirror (see RenderSecondView), not the normal UI pass
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "DrawMenu"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeDrawMenu))
        );
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        try
        {
            if (config.UiScale <= 0f || config.UiScale > 2f)
            {
                config.UiScale = 1f;
            }
            // dialogue boxes are a fixed 1240 UI pixels wide; shrink the UI while one is open so it fits
            dialogueUiScale = Math.Min(config.UiScale, renderWidth / 1280f);
            hudLift = config.HudLift;
            window = new VerticalWindow(
                "Stardew Valley Vertical View",
                config.WindowWidth, config.WindowHeight,
                renderWidth, renderHeight,
                config.WindowX, config.WindowY,
                message => Monitor.Log(message, LogLevel.Error));
            for (int i = 0; i < 2; i++)
            {
                targets[i] = new RenderTarget2D(Game1.graphics.GraphicsDevice, renderWidth, renderHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
            // big enough for the dialogue scale AND for menu frames rendered at the main window's UI width
            int menuUiWidth = Game1.uiViewport.Width;
            int menuUiHeight = (int)Math.Ceiling(renderHeight * (double)menuUiWidth / renderWidth);
            uiTarget = new RenderTarget2D(
                Game1.graphics.GraphicsDevice,
                Math.Max((int)Math.Ceiling(renderWidth / dialogueUiScale), menuUiWidth),
                Math.Max((int)Math.Ceiling(renderHeight / dialogueUiScale), menuUiHeight),
                false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        }
        catch (Exception exception)
        {
            Monitor.Log("Vertical window failed: " + exception, LogLevel.Error);
            window = null;
        }
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        lightmap?.Dispose();
        lightmap = null;
    }

    private static void BeforeDraw()
    {
        instance?.ReadBackAndPresent();
    }

    private static void AfterDraw(GameTime gameTime)
    {
        instance?.RenderSecondView(gameTime);
    }

    /// <summary>Hand LAST tick's finished render target texture to the present thread. The game's own
    /// main-window present sits between that render and this call, so the texture is guaranteed complete.
    /// No pixels are read back; this costs one reflection field read.</summary>
    private void ReadBackAndPresent()
    {
        RenderTarget2D? previous = targets[flip ^ 1];
        if (window == null || previous == null || !secondFrameReady)
        {
            return;
        }
        secondFrameReady = false;
        window.Present((int)GlTextureField.GetValue(previous)!);
    }

    private static void BeforeDrawHud()
    {
        if (renderingSecond)
        {
            Game1.graphics.GraphicsDevice.Viewport = new Viewport(0, 0, effectiveUiWidth, Math.Max(64, effectiveUiHeight - hudLift));
        }
    }

    private static void AfterDrawHud()
    {
        if (renderingSecond)
        {
            Game1.graphics.GraphicsDevice.Viewport = new Viewport(0, 0, effectiveUiWidth, effectiveUiHeight);
        }
    }

    private static bool SkipInSecondView()
    {
        return !renderingSecond;
    }

    private static bool BeforeDrawMenu()
    {
        return !(renderingSecond && suppressMenuDraw);
    }

    private void EnsureUiTargetSize(int width, int height)
    {
        if (uiTarget != null && width <= uiTarget.Width && height <= uiTarget.Height)
        {
            return;
        }
        RenderTarget2D grown = new RenderTarget2D(
            Game1.graphics.GraphicsDevice,
            Math.Max(uiTarget?.Width ?? 0, width),
            Math.Max(uiTarget?.Height ?? 0, height),
            false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        uiTarget?.Dispose();
        uiTarget = grown;
    }

    /// <summary>Draw the open menu into the UI buffer at the MAIN window's layout (so its cached positions
    /// and scissor rects are all valid), then composite just the menu's region onto the mirror, enlarged and
    /// centered over a full-screen fade. The HUD layer is untouched, so nothing else moves.</summary>
    private void DrawMenuPass(GraphicsDevice device, RenderTarget2D target, IClickableMenu menu, Rectangle mainBounds)
    {
        if (uiTarget == null)
        {
            return;
        }
        Game1.uiViewport = new xTile.Dimensions.Rectangle(0, 0, mainBounds.Width, mainBounds.Height);
        bool oldClearBackgrounds = Game1.options.showClearBackgrounds;
        Game1.options.showClearBackgrounds = true; // suppress the menu's own screen fade; we draw our own below
        device.SetRenderTarget(uiTarget);
        device.Clear(Color.Transparent);
        try
        {
            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            for (IClickableMenu? current = menu; current != null; current = current.GetChildMenu())
            {
                current.draw(Game1.spriteBatch);
            }
            Game1.spriteBatch.End();
        }
        finally
        {
            Game1.options.showClearBackgrounds = oldClearBackgrounds;
        }
        // the active page (e.g. the map) can be larger than the menu frame; include both
        Rectangle box = new Rectangle(menu.xPositionOnScreen, menu.yPositionOnScreen, menu.width, menu.height);
        if (menu is GameMenu gameMenu && gameMenu.currentTab < gameMenu.pages.Count)
        {
            IClickableMenu page = gameMenu.pages[gameMenu.currentTab];
            box = Rectangle.Union(box, new Rectangle(page.xPositionOnScreen, page.yPositionOnScreen, page.width, page.height));
        }
        // menus draw tabs/buttons/titles outside their box; pad generously, then clamp to the layout
        Rectangle source = new Rectangle(box.X - 160, box.Y - 128, box.Width + 352, box.Height + 256);
        source = Rectangle.Intersect(source, new Rectangle(0, 0, mainBounds.Width, mainBounds.Height));
        if (source.Width <= 0 || source.Height <= 0)
        {
            return;
        }
        float scale = Math.Min(2f, Math.Min((renderWidth - 48f) / source.Width, (renderHeight - 48f) / source.Height));
        int destWidth = (int)(source.Width * scale);
        int destHeight = (int)(source.Height * scale);
        device.SetRenderTarget(target);
        Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone);
        Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, renderWidth, renderHeight), Color.Black * 0.4f);
        Game1.spriteBatch.Draw(uiTarget, new Rectangle((renderWidth - destWidth) / 2, (renderHeight - destHeight) / 2, destWidth, destHeight), source, Color.White);
        Game1.spriteBatch.End();
    }

    private static void BeforeToolbarDraw()
    {
        if (renderingSecond)
        {
            Game1.graphics.GraphicsDevice.Viewport = new Viewport(0, 0, effectiveUiWidth, effectiveUiHeight);
        }
    }

    private void RenderSecondView(GameTime gameTime)
    {
        RenderTarget2D? target = targets[flip];
        if (window == null || target == null || uiTarget == null || !Context.IsWorldReady || Game1.currentLocation == null)
        {
            return;
        }
        if (config.FrameInterval > 1 && ++frameCounter % config.FrameInterval != 0)
        {
            return;
        }
        Game1 game = Game1.game1;
        GraphicsDevice device = Game1.graphics.GraphicsDevice;
        xTile.Dimensions.Rectangle oldViewport = Game1.viewport;
        xTile.Dimensions.Rectangle oldUiViewport = Game1.uiViewport;
        bool oldHud = Game1.displayHUD;
        bool oldScreenshot = game.takingMapScreenshot;
        bool oldIsDrawing = game.isDrawing;
        float oldZoom = Game1.options.baseZoomLevel;
        float oldUiScale = Game1.options.baseUIScale;
        RenderTarget2D? oldLightmap = (RenderTarget2D?)LightmapField.GetValue(null);
        RenderTarget2D? oldUiScreen = (RenderTarget2D?)UiScreenField.GetValue(game);
        Viewport oldDevice = device.Viewport;
        // dialogue boxes cache their centered position from uiViewport, so re-center them for our viewport and back
        DialogueBox? dialogue = Game1.activeClickableMenu as DialogueBox;
        // drawing HUD menus for our viewport moves their stored click positions; input runs before the
        // main draw restores them next tick, so snapshot the toolbar dock and put everything back after
        Toolbar? toolbar = null;
        int toolbarY = 0;
        foreach (IClickableMenu onScreen in Game1.onScreenMenus)
        {
            if (onScreen is Toolbar t)
            {
                toolbar = t;
                toolbarY = t.yPositionOnScreen;
                break;
            }
        }
        Rectangle mainBounds = new Rectangle(oldUiViewport.X, oldUiViewport.Y, oldUiViewport.Width, oldUiViewport.Height);
        // HUD always renders at the configured scale so it never moves when a menu opens;
        // non-dialogue menus get their own pass at main-window layout, composited enlarged
        float uiScale = dialogue != null ? dialogueUiScale : config.UiScale;
        IClickableMenu? openMenu = Game1.activeClickableMenu;
        suppressMenuDraw = openMenu != null && dialogue == null;
        effectiveUiWidth = (int)Math.Ceiling(renderWidth / uiScale);
        effectiveUiHeight = (int)Math.Ceiling(renderHeight / uiScale);
        EnsureUiTargetSize(
            Math.Max(effectiveUiWidth, suppressMenuDraw ? mainBounds.Width : 0),
            Math.Max(effectiveUiHeight, suppressMenuDraw ? mainBounds.Height : 0));
        if (openMenu != null && openMenu.GetType() != lastLoggedMenuType)
        {
            lastLoggedMenuType = openMenu.GetType();
            Monitor.Log(
                $"mirror menu layout: {lastLoggedMenuType.Name} at ({openMenu.xPositionOnScreen},{openMenu.yPositionOnScreen}) size {openMenu.width}x{openMenu.height}; " +
                $"mainUi {mainBounds.Width}x{mainBounds.Height}, uiScale {uiScale:F4}, effUi {effectiveUiWidth}x{effectiveUiHeight}, " +
                $"uiTarget {uiTarget.Width}x{uiTarget.Height}, render {renderWidth}x{renderHeight}, gameUiScaleOption {oldUiScale:F2}, gameZoom {oldZoom:F2}",
                LogLevel.Debug);
        }
        Rectangle secondBounds = new Rectangle(0, 0, effectiveUiWidth, effectiveUiHeight);
        try
        {
            Point center = Game1.player.StandingPixel;
            int mapWidth = Game1.currentLocation.map.Layers[0].LayerWidth * 64;
            int mapHeight = Game1.currentLocation.map.Layers[0].LayerHeight * 64;
            // clamp to the map edge like the main camera; center small maps
            int viewX = mapWidth <= renderWidth
                ? -(renderWidth - mapWidth) / 2
                : Math.Clamp(center.X - renderWidth / 2, 0, mapWidth - renderWidth);
            int viewY = mapHeight <= renderHeight
                ? -(renderHeight - mapHeight) / 2
                : Math.Clamp(center.Y - renderHeight / 2, 0, mapHeight - renderHeight);
            Game1.viewport = new xTile.Dimensions.Rectangle(viewX, viewY, renderWidth, renderHeight);
            Game1.displayHUD = config.ShowHud;
            game.takingMapScreenshot = !config.ShowHud;
            Game1.options.baseZoomLevel = 1f;
            Game1.options.baseUIScale = uiScale;
            LightmapField.SetValue(null, lightmap);
            AllocateLightmap.Invoke(null, new object[] { renderWidth, renderHeight });
            lightmap = (RenderTarget2D?)LightmapField.GetValue(null);

            // route UI drawing into our own correctly-sized buffer, like the game's own uiScreen path
            game.isDrawing = true;
            renderingSecond = true;
            UiScreenField.SetValue(game, uiTarget);
            device.SetRenderTarget(uiTarget);
            device.Clear(Color.Transparent);
            device.SetRenderTarget(null);

            if (dialogue != null)
            {
                Game1.uiViewport = new xTile.Dimensions.Rectangle(0, 0, effectiveUiWidth, effectiveUiHeight);
                dialogue.gameWindowSizeChanged(mainBounds, secondBounds);
            }

            DrawMethod.Invoke(game, new object[] { gameTime, target });

            // _draw can leave Push/PopUIMode unbalanced (the game's own Draw corrects this too);
            // a stuck uiMode makes the MAIN window keep our uiViewport and lose its HUD
            while (Game1.uiModeCount < 0)
            {
                Game1.PushUIMode();
            }
            while (Game1.uiModeCount > 0)
            {
                Game1.PopUIMode();
            }

            // composite the UI buffer onto the world, scaled down
            device.SetRenderTarget(target);
            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone);
            Game1.spriteBatch.Draw(uiTarget, new Rectangle(0, 0, renderWidth, renderHeight), new Rectangle(0, 0, effectiveUiWidth, effectiveUiHeight), Color.White);
            Game1.spriteBatch.End();

            if (suppressMenuDraw && openMenu != null)
            {
                DrawMenuPass(device, target, openMenu, mainBounds);
            }
            device.SetRenderTarget(null);

            // readback + present happen next tick in BeforeDraw, while the GPU is idle
            flip ^= 1;
            secondFrameReady = true;
        }
        catch (Exception exception)
        {
            Monitor.Log("Vertical view render failed, disabling: " + exception, LogLevel.Error);
            window.Dispose();
            window = null;
        }
        finally
        {
            renderingSecond = false;
            UiScreenField.SetValue(game, oldUiScreen);
            game.isDrawing = oldIsDrawing;
            LightmapField.SetValue(null, oldLightmap);
            Game1.options.baseUIScale = oldUiScale;
            Game1.options.baseZoomLevel = oldZoom;
            game.takingMapScreenshot = oldScreenshot;
            Game1.displayHUD = oldHud;
            Game1.uiViewport = oldUiViewport;
            Game1.viewport = oldViewport;
            device.Viewport = oldDevice;
            dialogue?.gameWindowSizeChanged(secondBounds, mainBounds);
            if (toolbar != null)
            {
                toolbar.yPositionOnScreen = toolbarY;
                toolbar.gameWindowSizeChanged(mainBounds, mainBounds); // rebuilds button bounds from uiViewport + yPositionOnScreen
            }
            DayTimeUpdatePosition.Invoke(Game1.dayTimeMoneyBox, null);
        }
    }
}
