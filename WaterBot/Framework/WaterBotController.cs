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
        private enum State { Idle, Planning, Ready, Moving, UsingTool }

        /// <summary>Watering costs up to 2 stamina, so never water at or below this.
        /// Watering at 2 or 1 could reach 0 or less, which exhausts the farmer.
        /// Walking and refilling cost no stamina and are not limited by it.</summary>
        private const float MinStamina = 2f;

        private readonly IModHelper helper;
        private State state;
        public bool active => this.state != State.Idle;
        private Map map = new();
        /// <summary>The whole run's watering stops, in walking order.</summary>
        private List<ActionableTile> stops = new();
        private int currentStop;
        private ActionableTile? refillStation;
        private bool finishingRefill;
        private console? console;
        private Farmer? farmer;
        private GameLocation? location;
        private WateringCan? can;
        private PathFindController? movement;
        private AnimatedSprite.endOfAnimationBehavior? toolEffect;
        private long runId;
        private Point destination;
        private Point target;
        private Vector2 lastPosition;
        private int stalledTicks;
        private int toolTicks;
        private bool effectApplied;
        private readonly Dictionary<Point, int> attempts = new();

        public WaterBotController(IModHelper helper) => this.helper = helper;

        public void start(console console, bool showMessage = true)
        {
            this.cancel();
            if (!Context.IsWorldReady || Game1.player.CurrentTool is not WateringCan wateringCan
                || Game1.player.UsingTool || Game1.player.toolOverrideFunction != null)
                return;
            if (Game1.player.Stamina <= MinStamina)
            {
                this.message("process.exhausted", 3);
                return;
            }

            this.console = console;
            this.farmer = Game1.player;
            this.location = Game1.currentLocation;
            this.can = wateringCan;
            this.attempts.Clear();
            this.state = State.Planning;
            this.guarded(() =>
            {
                this.plan();
                if (this.stops.Count == 0)
                {
                    this.finish();
                    return;
                }
                this.state = State.Ready;
                if (showMessage) this.message("process.start", 2);
            });
        }

        private void plan()
        {
            this.map = new Map();
            this.map.loadMap();
            this.stops = this.map.planStops(this.console!);
            this.currentStop = 0;
            this.refillStation = null;
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
                // Paused/menu time must not consume movement or animation timeouts.
                if (Game1.activeClickableMenu != null || !Game1.shouldTimePass()) return;

                switch (this.state)
                {
                    case State.Ready: this.advance(); break;
                    case State.Moving: this.checkMovement(); break;
                    case State.UsingTool: this.checkTool(); break;
                }
            });
        }

        private void advance()
        {
            if (this.farmer!.UsingTool) return;
            if (this.refillStation != null)
            {
                this.useNext(this.refillStation);
                return;
            }
            while (this.currentStop < this.stops.Count && this.stops[this.currentStop].isDone())
                this.currentStop++;
            if (this.currentStop >= this.stops.Count)
            {
                if (WaterBot.config.RefillOnFinish && !this.can!.IsBottomless && !this.farmer.hasWateringCanEnchantment
                    && this.can.WaterLeft < this.can.waterCanMax
                    && this.can.WaterLeft < this.can.waterCanMax * Math.Clamp(WaterBot.config.RefillIfLower, 0, 100) / 100f)
                    this.refill(true);
                else
                    this.finish();
                return;
            }
            ActionableTile stop = this.stops[this.currentStop];
            if (this.can!.WaterLeft <= 0 && !this.farmer.hasWateringCanEnchantment)
            {
                this.refill(false);
                return;
            }
            this.useNext(stop);
        }

        private void useNext(ActionableTile stop)
        {
            // Only watering costs stamina; walking and refilling do not. A tired farmer can't
            // water anything at a watering stop, so stop here instead of walking to it first.
            if (stop.getAction() == ActionableTile.Action.Water && this.tooTired())
            {
                this.exhausted();
                return;
            }
            if (this.farmer!.TilePoint != stop.getStand())
            {
                this.moveTo(stop.getStand());
                return;
            }
            Point next = stop.Pop();
            if (next.X == -1) return;
            if (stop.getAction() == ActionableTile.Action.Water && !Map.tileNeedsWatering(next.X, next.Y))
                return; // Another stop, sprinkler, or player may already have watered this crop.
            if (Math.Abs(next.X - this.farmer.TilePoint.X) > 1 || Math.Abs(next.Y - this.farmer.TilePoint.Y) > 1)
                throw new InvalidOperationException($"Target {next} is outside watering range.");
            this.beginTool(next);
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
            this.refillStation = this.map.getClosestRefill(this.map.map[player.Y][player.X], this.console!);
            if (this.refillStation == null)
            {
                this.cancel();
                this.message("process.waterless", 3);
            }
        }

        private void beginTool(Point point)
        {
            Farmer player = this.farmer!;
            WateringCan tool = this.can!;
            GameLocation currentLocation = this.location!;
            if (player.toolOverrideFunction != null)
                throw new InvalidOperationException("Another tool action is already in progress.");
            this.target = point;
            this.effectApplied = false;
            this.toolTicks = 0;
            long id = this.runId;
            bool watering = this.refillStation == null;
            // The vanilla animation invokes Farmer.useTool. Its default targeting can ignore
            // lastClick with a controller/hidden cursor, so supply the exact tile at that moment.
            this.toolEffect = who => this.guarded(() =>
            {
                if (id != this.runId || this.state != State.UsingTool || this.effectApplied) return;
                if (who != player || who.currentLocation != currentLocation || who.CurrentTool != tool)
                    throw new InvalidOperationException("Player, location, or tool changed during watering.");
                // Last check before watering spends stamina; checkTool reports exhaustion once the
                // animation ends. Refilling costs no stamina, so it is never blocked here.
                if (watering && who.Stamina <= MinStamina) return;
                this.effectApplied = true;
                if (watering && !Map.tileNeedsWatering(point.X, point.Y)) return;
                float stamina = who.Stamina;
                who.toolPower.Value = 0;
                tool.DoFunction(currentLocation, point.X * Game1.tileSize + Game1.tileSize / 2,
                    point.Y * Game1.tileSize + Game1.tileSize / 2, 0, who);
                who.checkForExhaustion(stamina);
            });
            player.toolOverrideFunction = this.toolEffect;
            player.FacingDirection = point.Y < player.TilePoint.Y ? 0 : point.Y > player.TilePoint.Y ? 2
                : point.X < player.TilePoint.X ? 3 : 1;
            player.FarmerSprite.SetOwner(player);
            player.Halt();
            player.CanMove = false;
            player.UsingTool = true;
            player.canReleaseTool = false;
            player.toolPower.Value = 0;
            player.lastClick = new Vector2(point.X * Game1.tileSize + Game1.tileSize / 2,
                point.Y * Game1.tileSize + Game1.tileSize / 2);
            this.state = State.UsingTool;
            tool.Update(player.FacingDirection, 0, player);
            tool.endUsing(currentLocation, player);
        }

        private void checkTool()
        {
            if (++this.toolTicks >= 300)
                throw new InvalidOperationException("Watering animation did not complete within five seconds.");
            if (this.farmer!.UsingTool) return;
            this.releaseToolEffect();
            bool refilling = this.refillStation != null;
            bool success = this.effectApplied && (refilling
                ? this.can!.WaterLeft >= this.can.waterCanMax
                : !Map.tileNeedsWatering(this.target.X, this.target.Y));
            if (!success)
            {
                if (!refilling && this.tooTired())
                {
                    this.exhausted();
                    return;
                }
                int count = this.attempts.GetValueOrDefault(this.target) + 1;
                this.attempts[this.target] = count;
                if (count >= 2)
                    throw new InvalidOperationException($"Tool use failed twice at {this.target}.");
                // Revalidate before retrying. No snapshot is marked complete on a failed action.
                if (!refilling && !Map.tileNeedsWatering(this.target.X, this.target.Y))
                {
                    this.state = State.Ready;
                    return;
                }
                this.beginTool(this.target);
                return;
            }
            if (!refilling)
                this.map.map[this.target.Y][this.target.X].waterable = false;
            else
            {
                this.refillStation = null;
                if (this.finishingRefill)
                {
                    this.finish();
                    return;
                }
                if (WaterBot.config.RedoPathOnRefill) this.plan();
            }
            this.state = State.Ready;
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
            this.state = State.Idle;
            if (this.farmer != null)
            {
                if (this.movement != null && this.farmer.controller == this.movement)
                {
                    this.farmer.controller = null;
                    this.farmer.Halt();
                }
                if (this.toolEffect != null && this.farmer.toolOverrideFunction == this.toolEffect)
                {
                    this.farmer.FarmerSprite.CurrentAnimation = null;
                    this.farmer.lastClick = Vector2.Zero;
                    this.farmer.stopJittering();
                    Farmer.canMoveNow(this.farmer);
                }
            }
            this.releaseToolEffect();
            this.movement = null;
            this.refillStation = null;
            this.finishingRefill = false;
        }

        private bool tooTired() => this.farmer!.Stamina <= MinStamina;

        private void exhausted()
        {
            this.console?.Invoke($"Stamina is {this.farmer?.Stamina}; stopping before another action.");
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
