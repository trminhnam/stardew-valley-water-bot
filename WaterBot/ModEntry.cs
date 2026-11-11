using BotFramework;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using WaterBot.Framework;

namespace WaterBot
{
    /// <summary>
    /// The mod entry point.
    /// </summary>
    public class WaterBot : Mod
    {
        internal static Config config = new();
        private WaterBotController? bot;

        /// <summary>
        /// The mod entry point, called after the mod is first loaded.
        /// </summary>
        /// 
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<Config>();
            Logger.SetMonitor(Monitor);
            this.bot = new WaterBotController(helper);
            // Set static reference to monitor for logging.

            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.GameLoop.UpdateTicked += (s, e) => this.bot?.tick();
            helper.Events.GameLoop.ReturnedToTitle += (s, e) => this.bot?.cancel();
            helper.Events.GameLoop.DayEnding += (s, e) => this.bot?.cancel();
            helper.Events.Player.Warped += (s, e) => { if (e.IsLocalPlayer) this.bot?.cancel(); };
            helper.Events.GameLoop.GameLaunched += (s, e) => SetUpConfigMenu();
        }

        /// <summary>
        /// Raised after the player presses a button on the keyboard, controller, or mouse.
        /// </summary>
        /// 
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            // ignore if player hasn't loaded a save yet
            if (!Context.IsWorldReady)
                return;

            if (this.bot?.active == true)
            {
                Logger.Log("Player provided interrupt signal. Process stopped.");
                this.bot.stop();
            }
            else if (Context.IsPlayerFree && !Game1.player.UsingTool
                && Game1.player.toolOverrideFunction == null && e.Button.IsActionButton())
            {
                if (this.isWateringHoedDirt())
                {
                    Logger.Log("Player provided trigger to begin bot.");
                    this.Helper.Input.Suppress(e.Button);
                    this.verifyPatterns();
                    this.bot?.start(this.console);
                }
            }
        }

        /// <summary>
        /// Determines if the event was watering a tile of hoed dirt
        /// </summary>
        private bool isWateringHoedDirt()
        {
            // Start on a growing crop; leave ready-to-harvest crops to the normal action button.
            if (Game1.player?.CurrentItem is WateringCan)
            {
                // Find action tiles
                Vector2 mousePosition = Utility.PointToVector2(Game1.getMousePosition()) + new Vector2(Game1.viewport.X, Game1.viewport.Y);
                Vector2 toolLocation = Game1.player.GetToolLocation(mousePosition);
                Vector2 tile = Utility.clampToTile(toolLocation);

                List<Vector2> tileLocations = this.Helper.Reflection
                    .GetMethod(Game1.player.CurrentItem, "tilesAffected")
                    .Invoke<List<Vector2>>(new Vector2(tile.X / 64, tile.Y / 64), 0, Game1.player);

                foreach (Vector2 tileLocation in tileLocations)
                {
                    Vector2 rounded = new Vector2((float)Math.Round(tileLocation.X), (float)Math.Round(tileLocation.Y));

                    // If they just watered Hoe Dirt, return true
                    if (Game1.currentLocation?.terrainFeatures.ContainsKey(rounded) == true &&
                        Game1.currentLocation.terrainFeatures[rounded] is HoeDirt dirt &&
                        dirt.crop != null && !dirt.crop.dead.Value &&
                        Map.tileNeedsWatering((int)rounded.X, (int)rounded.Y) &&
                        ((dirt.crop.fullyGrown.Value &&
                        dirt.crop.dayOfCurrentPhase.Value > 0) || 
                            (dirt.crop.currentPhase.Value < dirt.crop.phaseDays.Count - 1)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool patternsChecked;

        /// <summary>
        /// Once per session, compare the bot's charged-watering patterns with the game's own
        /// Tool.tilesAffected. If they differ (e.g. after a game update), charged watering is
        /// disabled and the bot falls back to single tiles.
        /// </summary>
        private void verifyPatterns()
        {
            if (this.patternsChecked || Game1.player?.CurrentTool is not WateringCan can) return;
            this.patternsChecked = true;
            try
            {
                var method = this.Helper.Reflection.GetMethod(can, "tilesAffected");
                string? difference = CanProfile.VerifyPatterns(
                    (origin, power) => (method.Invoke<List<Vector2>>(new Vector2(origin.X, origin.Y), power, Game1.player) ?? new List<Vector2>())
                        .Select(v => new Point((int)Math.Round(v.X), (int)Math.Round(v.Y))),
                    Game1.player.FacingDirection);
                if (difference != null)
                {
                    CanProfile.PatternsVerified = false;
                    Logger.Log($"Watering patterns differ from the game ({difference}). Charged watering is disabled.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                CanProfile.PatternsVerified = false;
                Logger.Log($"Could not check watering patterns ({ex.Message}). Charged watering is disabled.", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Debug messages
        /// </summary>
        /// 
        /// <param name="message">Message text.</param>
        public void console(string message)
        {
            Logger.Log(message, LogLevel.Debug);
        }

        public class Config
        {
            // UseSmallGrouping was removed: one planner now covers the whole farm.
            // An old config.json that still contains it loads fine; the value is ignored.
            public bool RefillOnFinish { get; set; } = false;
            public int RefillIfLower { get; set; } = 95;
            public bool RedoPathOnRefill { get; set; } = false;

            /// <summary>Use upgraded cans' charged patterns (lines and squares) where they save stamina.</summary>
            public bool UseChargedWatering { get; set; } = true;

            /// <summary>Skip the hold time when charging (otherwise 600 ms per level, like a player).</summary>
            public bool InstantCharge { get; set; } = false;

            /// <summary>"Stamina" (least stamina per crop) or "Speed" (least time per crop).</summary>
            public string Optimize { get; set; } = "Stamina";
        }

        private void SetUpConfigMenu()
        {
            // get Generic Mod Config Menu's API (if it's installed)
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => config = new Config(),
                save: () => this.Helper.WriteConfig<Config>(config)
            );

            // add some config options
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => this.Helper.Translation.Get("config.refill_on_finish.name"),
                tooltip: () => this.Helper.Translation.Get("config.refill_on_finish.desc"),
                getValue: () => config.RefillOnFinish,
                setValue: (bool b) => config.RefillOnFinish = b
            );
            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => this.Helper.Translation.Get("config.refill_if_lower.name"),
                tooltip: () => this.Helper.Translation.Get("config.refill_if_lower.desc"),
                getValue: () => config.RefillIfLower,
                setValue: (int b) => config.RefillIfLower = b,
                min: 0,
                max: 100
            );
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => this.Helper.Translation.Get("config.redo_path_on_refill.name"),
                tooltip: () => this.Helper.Translation.Get("config.redo_path_on_refill.desc"),
                getValue: () => config.RedoPathOnRefill,
                setValue: (bool b) => config.RedoPathOnRefill = b
            );
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => this.Helper.Translation.Get("config.use_charged_watering.name"),
                tooltip: () => this.Helper.Translation.Get("config.use_charged_watering.desc"),
                getValue: () => config.UseChargedWatering,
                setValue: (bool b) => config.UseChargedWatering = b
            );
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => this.Helper.Translation.Get("config.instant_charge.name"),
                tooltip: () => this.Helper.Translation.Get("config.instant_charge.desc"),
                getValue: () => config.InstantCharge,
                setValue: (bool b) => config.InstantCharge = b
            );
            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => this.Helper.Translation.Get("config.optimize.name"),
                tooltip: () => this.Helper.Translation.Get("config.optimize.desc"),
                getValue: () => config.Optimize,
                setValue: (string v) => config.Optimize = v,
                allowedValues: new[] { "Stamina", "Speed" },
                formatAllowedValue: (string v) => this.Helper.Translation.Get($"config.optimize.{v.ToLowerInvariant()}")
            );
        }
    }
}