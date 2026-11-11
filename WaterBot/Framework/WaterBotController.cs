using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.Tools;

namespace WaterBot.Framework
{
    /// <summary>Runs navigation and tool use exclusively from the game's update loop.</summary>
    class WaterBotController
    {
        private enum State { Idle, Planning, Ready, Moving, Charging, UsingTool }

        /// <summary>Animation frames per facing direction for the watering can (Tool.beginUsing).</summary>
        private static readonly int[] chargeFrames = { 180, 172, 164, 188 };

        private readonly IModHelper helper;
        private State state;
        public bool active => this.state != State.Idle;

        /// <summary>Whether the farmer is currently holding the can to charge it.</summary>
        public bool charging => this.state == State.Charging;
        private Map map = new();
        private CanProfile profile = new();

        /// <summary>The whole run's can uses, in walking order.</summary>
        private List<WaterAction> actions = new();
        private int currentAction;
        private WaterAction? refillAction;
        private WaterAction? activeAction;
        private bool finishingRefill;
        private console? console;
        private Farmer? farmer;
        private GameLocation? location;
        private WateringCan? can;
        private PathFindController? movement;
        private AnimatedSprite.endOfAnimationBehavior? toolEffect;
        private long runId;
        private Point destination;
        private Vector2 lastPosition;
        private int stalledTicks;
        private int toolTicks;
        private int chargeTicks;
        private int chargeTotalTicks;
        private bool effectApplied;

        /// <summary>Failed attempts per crop. A crop that fails twice is skipped and reported at the end.</summary>
        private readonly Dictionary<Point, int> attempts = new();
        private readonly HashSet<Point> skipped = new();
        private int refillFailures;

        public WaterBotController(IModHelper helper) => this.helper = helper;

        private static Planner.Options planOptions() => new()
        {
            OptimizeSpeed = string.Equals(WaterBot.config.Optimize, "Speed", StringComparison.OrdinalIgnoreCase),
            InstantCharge = WaterBot.config.InstantCharge,
        };

        public void start(console console, bool showMessage = true)
        {
            this.cancel();
            if (!Context.IsWorldReady || Game1.player.CurrentTool is not WateringCan wateringCan
                || Game1.player.UsingTool || Game1.player.toolOverrideFunction != null)
                return;
            if (!CanProfile.Read(Game1.player, wateringCan, WaterBot.config.UseChargedWatering).CanAfford(Game1.player.Stamina, 0))
            {
                this.message("process.exhausted", 3);
                return;
            }

            this.console = console;
            this.farmer = Game1.player;
            this.location = Game1.currentLocation;
            this.can = wateringCan;
            this.attempts.Clear();
            this.skipped.Clear();
            this.refillFailures = 0;
            this.state = State.Planning;
            this.guarded(() =>
            {
                this.plan();
                if (this.actions.Count == 0)
                {
                    this.finish();
                    return;
                }
                this.state = State.Ready;
                if (showMessage) this.message("process.start", 2);
            });
        }

        /// <summary>(Re)plan every crop that still needs water, from where the farmer stands, with current stamina.</summary>
        private void plan()
        {
            this.map = new Map();
            this.map.loadMap();
            this.profile = CanProfile.Read(this.farmer!, this.can!, WaterBot.config.UseChargedWatering);
            this.actions = Planner.Plan(this.map, this.profile, this.farmer!.Stamina, planOptions(), this.console!, this.farmer.TilePoint, this.skipped);
            this.currentAction = 0;
            this.refillAction = null;
            this.finishingRefill = false;
        }

        public void tick()
        {
            if (!this.active) return;
            this.guarded(() =>
            {
                if (!Context.IsWorldReady || Game1.player != this.farmer || Game1.currentLocation != this.location
                    || this.farmer!.CurrentTool != this.can)
                {
                    this.cancel();
                    return;
                }
                // Paused/menu time must not consume movement, charge, or animation timeouts.
                if (Game1.activeClickableMenu != null || !Game1.shouldTimePass()) return;

                switch (this.state)
                {
                    case State.Ready: this.advance(); break;
                    case State.Moving: this.checkMovement(); break;
                    case State.Charging: this.checkCharge(); break;
                    case State.UsingTool: this.checkTool(); break;
                }
            });
        }

        private bool needsWater(Point tile) => !this.skipped.Contains(tile) && Map.tileNeedsWatering(tile.X, tile.Y);

        private void advance()
        {
            if (this.farmer!.UsingTool) return;
            if (this.refillAction != null)
            {
                this.useNext(this.refillAction);
                return;
            }

            // Skip uses whose crops are already wet (watered by another use, a sprinkler, or another player).
            while (this.currentAction < this.actions.Count && !this.actions[this.currentAction].Targets.Any(this.needsWater))
                this.currentAction++;
            if (this.currentAction >= this.actions.Count)
            {
                // Nothing left that stamina allows, but crops still need water: say why we stopped.
                if (!CanProfile.Read(this.farmer, this.can!, WaterBot.config.UseChargedWatering).CanAfford(this.farmer.Stamina, 0)
                    && this.map.waterableTiles.Any(t => this.needsWater(t.getPoint())))
                {
                    this.exhausted();
                    return;
                }
                if (WaterBot.config.RefillOnFinish && !this.can!.IsBottomless && !this.farmer.hasWateringCanEnchantment
                    && this.can.WaterLeft < this.can.waterCanMax
                    && this.can.WaterLeft < this.can.waterCanMax * Math.Clamp(WaterBot.config.RefillIfLower, 0, 100) / 100f)
                    this.refill(true);
                else
                    this.finish();
                return;
            }

            WaterAction next = this.actions[this.currentAction];
            this.profile = CanProfile.Read(this.farmer, this.can!, WaterBot.config.UseChargedWatering);

            // Stamina is checked before walking to a watering, at the charge level about to be used.
            bool affordable = this.profile.CanAfford(this.farmer.Stamina, next.Power);
            bool canWaterAtAll = this.profile.CanAfford(this.farmer.Stamina, 0);
            if (!affordable && canWaterAtAll)
            {
                this.plan(); // re-plan the rest with the charge levels still affordable
                return;
            }

            // Water: refill first if this use needs more than is left. Refilling costs no stamina,
            // so it isn't blocked by stamina (the bot then stops before the next watering if tired).
            if (!this.profile.Unlimited && this.can!.WaterLeft < this.profile.WaterUse(next.Power))
            {
                this.refill(false);
                return;
            }

            if (!affordable)
            {
                this.exhausted();
                return;
            }
            this.useNext(next);
        }

        private void useNext(WaterAction action)
        {
            if (this.farmer!.TilePoint != action.Stand)
            {
                this.moveTo(action.Stand);
                return;
            }
            if (action.Action == WaterAction.Kind.Water && action.Power == 0
                && (Math.Abs(action.Origin.X - action.Stand.X) > 1 || Math.Abs(action.Origin.Y - action.Stand.Y) > 1))
                throw new InvalidOperationException($"Target {action.Origin} is outside watering range.");

            if (action.Action == WaterAction.Kind.Water && action.Power > 0 && !WaterBot.config.InstantCharge)
                this.beginCharge(action);
            else
                this.release(action);
        }

        private void moveTo(Point stand)
        {
            if (!this.map.isReachable(stand) || Map.tileIsPassable(stand.Y, stand.X))
                throw new InvalidOperationException($"Watering position {stand} is blocked or unreachable.");
            this.destination = stand;
            this.movement = new PathFindController(this.farmer!, this.location!, stand, 2);
            if (this.movement.pathToEndPoint == null || this.movement.pathToEndPoint.Count == 0)
                throw new InvalidOperationException($"The game could not find a path to {stand}.");
            this.lastPosition = this.farmer!.Position;
            this.stalledTicks = 0;
            this.farmer.controller = this.movement;
            this.state = State.Moving;
        }

        private void checkMovement()
        {
            if (this.farmer!.controller != this.movement && this.farmer.controller != null)
                throw new InvalidOperationException("The bot's movement controller was replaced.");
            if (this.farmer.TilePoint == this.destination)
            {
                this.farmer.controller = null;
                this.farmer.Halt();
                this.movement = null;
                this.state = State.Ready;
                return;
            }
            if (this.farmer.controller == null || this.movement!.pathToEndPoint == null
                || this.movement.pathToEndPoint.Count == 0)
                throw new InvalidOperationException($"Movement ended before reaching {this.destination}.");
            this.stalledTicks = this.farmer.Position == this.lastPosition ? this.stalledTicks + 1 : 0;
            this.lastPosition = this.farmer.Position;
            if (this.stalledTicks >= 180)
                throw new InvalidOperationException($"Movement to {this.destination} stalled for three seconds.");
        }

        private void refill(bool finishing)
        {
            this.finishingRefill = finishing;
            Point player = this.farmer!.TilePoint;
            this.refillAction = this.map.getClosestRefill(this.map.map[player.Y][player.X], this.console!);
            if (this.refillAction == null)
            {
                this.cancel();
                this.message("process.waterless", 3);
            }
        }

        /// <summary>
        /// Hold the can like a player would: face the target, then raise the charge one level per
        /// 600 ms × AnimationSpeedModifier using the game's own Farmer.toolPowerIncrease (sound, flash, shake).
        /// </summary>
        private void beginCharge(WaterAction action)
        {
            Farmer player = this.farmer!;
            if (player.toolOverrideFunction != null)
                throw new InvalidOperationException("Another tool action is already in progress.");
            this.activeAction = action;
            // While charging, the game may still end the tool use itself (e.g. an event starts). Its
            // Farmer.useTool would then water wherever the mouse points at the charged level, so hook it
            // and stop the run instead.
            long id = this.runId;
            this.toolEffect = who => this.guarded(() =>
            {
                if (id == this.runId && this.state == State.Charging)
                    throw new InvalidOperationException("The game ended the tool use while charging.");
            });
            player.toolOverrideFunction = this.toolEffect;
            player.FacingDirection = action.Facing;
            player.FarmerSprite.SetOwner(player);
            player.Halt();
            player.CanMove = false;
            player.UsingTool = true;
            player.canReleaseTool = false;
            player.toolPower.Value = 0;
            player.jitterStrength = 0.25f;
            player.FarmerSprite.setCurrentFrame(chargeFrames[action.Facing]);
            this.can!.Update(action.Facing, 0, player);
            this.chargeTicks = 0;
            this.chargeTotalTicks = 0;
            this.state = State.Charging;
        }

        private void checkCharge()
        {
            WaterAction action = this.activeAction!;
            int limit = action.Power * this.profile.ChargeTicksPerLevel + 120;
            if (++this.chargeTotalTicks > limit)
                throw new InvalidOperationException("Charging did not finish in time.");
            if (this.farmer!.FacingDirection != action.Facing)
                throw new InvalidOperationException("Facing changed while charging.");
            if (++this.chargeTicks >= this.profile.ChargeTicksPerLevel)
            {
                this.chargeTicks = 0;
                this.farmer.toolPowerIncrease();
            }
            if (this.farmer.toolPower.Value >= action.Power)
                this.release(action);
        }

        /// <summary>
        /// Play the game's watering animation. Its Farmer.useTool callback runs toolOverrideFunction,
        /// which applies the can at the planned origin and charge level (docs/game-mechanics.md).
        /// </summary>
        private void release(WaterAction action)
        {
            Farmer player = this.farmer!;
            WateringCan tool = this.can!;
            GameLocation currentLocation = this.location!;
            this.releaseToolEffect(); // the charging guard, if any
            if (player.toolOverrideFunction != null)
                throw new InvalidOperationException("Another tool action is already in progress.");
            this.activeAction = action;
            this.effectApplied = false;
            this.toolTicks = 0;
            long id = this.runId;
            bool watering = action.Action == WaterAction.Kind.Water;
            int power = watering ? action.Power : 0;
            int facing = action.Facing >= 0 ? action.Facing : player.FacingDirection;
            CanProfile profile = this.profile;
            this.toolEffect = who => this.guarded(() =>
            {
                if (id != this.runId || this.state != State.UsingTool || this.effectApplied) return;
                if (who != player || who.currentLocation != currentLocation || who.CurrentTool != tool)
                    throw new InvalidOperationException("Player, location, or tool changed during watering.");
                // Last stamina check before it's spent. Refilling costs none, so it's never blocked.
                if (watering && !profile.CanAfford(who.Stamina, power)) return;
                this.effectApplied = true;
                if (watering && !action.Targets.Any(this.needsWater)) return;
                float stamina = who.Stamina;
                // The pattern points the way the farmer faces (Tool.tilesAffected), so make sure it's the planned way.
                who.FacingDirection = facing;
                who.toolPower.Value = power;
                tool.DoFunction(currentLocation, action.Origin.X * Game1.tileSize + Game1.tileSize / 2,
                    action.Origin.Y * Game1.tileSize + Game1.tileSize / 2, power, who);
                who.toolPower.Value = 0;
                who.checkForExhaustion(stamina);
            });
            player.toolOverrideFunction = this.toolEffect;
            player.FacingDirection = facing;
            player.FarmerSprite.SetOwner(player);
            player.Halt();
            player.CanMove = false;
            player.UsingTool = true;
            player.canReleaseTool = false;
            player.toolPower.Value = power;
            player.lastClick = new Vector2(action.Origin.X * Game1.tileSize + Game1.tileSize / 2,
                action.Origin.Y * Game1.tileSize + Game1.tileSize / 2);
            this.state = State.UsingTool;
            tool.Update(facing, 0, player);
            tool.endUsing(currentLocation, player);
        }

        private void checkTool()
        {
            if (++this.toolTicks >= 300)
                throw new InvalidOperationException("Watering animation did not complete within five seconds.");
            if (this.farmer!.UsingTool) return;
            this.releaseToolEffect();
            this.farmer.toolPower.Value = 0;
            WaterAction action = this.activeAction!;
            this.activeAction = null;

            if (action.Action == WaterAction.Kind.Refill)
            {
                if (!this.effectApplied || this.can!.WaterLeft < this.can.waterCanMax)
                {
                    if (++this.refillFailures >= 2)
                        throw new InvalidOperationException($"Refilling failed twice at {action.Origin}.");
                    this.release(action);
                    return;
                }
                this.refillFailures = 0;
                this.refillAction = null;
                if (this.finishingRefill)
                {
                    this.finish();
                    return;
                }
                if (WaterBot.config.RedoPathOnRefill) this.plan();
                this.state = State.Ready;
                return;
            }

            if (!this.effectApplied && !this.profile.CanAfford(this.farmer.Stamina, action.Power))
            {
                // Stamina dropped during the animation: re-plan at a lower level, or stop.
                if (!this.profile.CanAfford(this.farmer.Stamina, 0)) this.exhausted();
                else
                {
                    this.plan();
                    this.state = State.Ready;
                }
                return;
            }

            var dry = new List<Point>();
            foreach (Point target in action.Targets)
            {
                if (this.needsWater(target)) dry.Add(target);
                else this.map.map[target.Y][target.X].waterable = false;
            }
            this.currentAction++;
            this.state = State.Ready;
            if (dry.Count == 0) return;

            // Some crops are still dry: count the failure, skip crops that failed twice, and re-plan the rest.
            foreach (Point target in dry)
            {
                int count = this.attempts.GetValueOrDefault(target) + 1;
                this.attempts[target] = count;
                if (count >= 2)
                {
                    this.skipped.Add(target);
                    this.console?.Invoke($"Crop {target} is still dry after two attempts. Skipping it.");
                }
            }
            this.plan();
        }

        private void releaseToolEffect()
        {
            if (this.farmer != null && this.toolEffect != null && this.farmer.toolOverrideFunction == this.toolEffect)
            {
                this.farmer.toolOverrideFunction = null;
                this.farmer.lastClick = Vector2.Zero;
            }
            this.toolEffect = null;
        }

        public void cancel()
        {
            this.runId++;
            State previous = this.state;
            this.state = State.Idle;
            if (this.farmer != null)
            {
                if (this.movement != null && this.farmer.controller == this.movement)
                {
                    this.farmer.controller = null;
                    this.farmer.Halt();
                }
                bool ownsTool = this.toolEffect != null && this.farmer.toolOverrideFunction == this.toolEffect;
                if (ownsTool || previous == State.Charging)
                {
                    if (previous == State.UsingTool) this.farmer.FarmerSprite.CurrentAnimation = null;
                    this.farmer.lastClick = Vector2.Zero;
                    this.farmer.toolPower.Value = 0;
                    this.farmer.stopJittering();
                    Farmer.canMoveNow(this.farmer);
                }
            }
            this.releaseToolEffect();
            this.movement = null;
            this.refillAction = null;
            this.activeAction = null;
            this.finishingRefill = false;
        }

        private void exhausted()
        {
            this.console?.Invoke($"Stamina is {this.farmer?.Stamina}; stopping before another watering.");
            this.cancel();
            this.message("process.exhausted", 3);
        }

        public void stop()
        {
            this.cancel();
            this.message("process.interrupt", 1);
        }

        private void finish()
        {
            int remaining = this.map.waterableTiles.Count(t => Map.tileNeedsWatering(t.x, t.y));
            this.cancel();
            if (remaining > 0)
            {
                this.console?.Invoke($"Finished with {remaining} crops still needing water (unreachable or skipped).");
                Game1.addHUDMessage(new HUDMessage(this.helper.Translation.Get("process.remaining", new { count = remaining }), 3));
            }
            else this.message("process.end", 1);
        }

        private void guarded(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                this.cancel();
                BotFramework.Logger.Log($"WaterBot stopped safely: {ex}", LogLevel.Error);
                if (Context.IsWorldReady) this.message("process.failed", 3);
            }
        }

        private void message(string key, int type) => Game1.addHUDMessage(new HUDMessage(this.helper.Translation.Get(key), type));
    }
}
