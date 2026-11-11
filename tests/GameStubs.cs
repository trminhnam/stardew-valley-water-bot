// Minimal game/API doubles for deterministic tests of the linked production code.
// These do not emulate Stardew's rendering, networking, or animation engine.
namespace Microsoft.Xna.Framework
{
    public readonly record struct Point(int X, int Y);
    public readonly record struct Vector2(float X, float Y)
    {
        public static Vector2 Zero => new(0, 0);
    }
    public readonly record struct Rectangle(int X, int Y, int Width, int Height);
}
namespace StardewModdingAPI
{
    public enum LogLevel { Debug, Info, Trace, Error }
    public interface IMonitor { void Log(string message, LogLevel level); }
    public interface IModHelper { Translations Translation { get; } }
    public sealed class Translations
    {
        public string Get(string key, object? tokens = null) => key;
    }
    public static class Context { public static bool IsWorldReady = true; }
}
namespace WaterBot
{
    public class WaterBot
    {
        internal static Config config = new();
        public class Config
        {
            public bool RefillOnFinish { get; set; }
            public int RefillIfLower { get; set; } = 95;
            public bool RedoPathOnRefill { get; set; }
            public bool UseChargedWatering { get; set; } = true;
            public bool InstantCharge { get; set; }
            public string Optimize { get; set; } = "Stamina";
        }
    }
}
namespace StardewValley
{
    using Microsoft.Xna.Framework;
    public class NetValue<T>(T value) { public T Value = value; }
    public class Crop
    {
        public NetValue<bool> dead = new(false), fullyGrown = new(false);
        public NetValue<int> dayOfCurrentPhase = new(0), currentPhase = new(0);
        public List<int> phaseDays = new() { 1, 1, 1 };
        public bool Regrows;
        public bool RegrowsAfterHarvest() => this.Regrows;
    }
    public class Layer { public int LayerHeight = 8, LayerWidth = 8; }
    public class TestMap { public List<Layer> Layers = new() { new() }; }
    public class GameLocation
    {
        public TestMap map = new();
        public Dictionary<Vector2, TerrainFeatures.TerrainFeature> terrainFeatures = new();
        public HashSet<Point> Blocks = new(), Water = new();
        public bool FailPaths, IgnoreWatering, ThrowOnScan;
        public HashSet<Point> IgnoreTiles = new();
        public bool isTileHoeDirt(Vector2 tile) => this.terrainFeatures.ContainsKey(tile);
        public bool CanRefillWateringCanOnTile(int x, int y) => this.Water.Contains(new(x, y));
        public bool isCollidingPosition(Rectangle rectangle, object viewport, bool isFarmer, int unused, bool glider, Farmer farmer)
        {
            if (this.ThrowOnScan) throw new InvalidOperationException("Injected map failure");
            return this.Blocks.Contains(new(rectangle.X / 64, rectangle.Y / 64));
        }
    }
    public class Character { }
    public class AnimatedSprite
    {
        public delegate void endOfAnimationBehavior(Farmer who);
    }
    public class FarmerSprite
    {
        public object? CurrentAnimation;
        public int CurrentFrame;
        public void SetOwner(Farmer farmer) { }
        public void setCurrentFrame(int which) => this.CurrentFrame = which;
    }
    public class Farmer : Character
    {
        public Point TilePoint
        {
            get => new((int)this.Position.X / 64, (int)this.Position.Y / 64);
            set => this.Position = new(value.X * 64, value.Y * 64);
        }
        public Vector2 Position;
        public Pathfinding.PathFindController? controller;
        public Tools.WateringCan? CurrentTool = new();
        public GameLocation currentLocation = null!;
        public AnimatedSprite.endOfAnimationBehavior? toolOverrideFunction;
        public FarmerSprite FarmerSprite = new();
        public NetValue<int> toolPower = new(0);
        public bool UsingTool, CanMove = true, canReleaseTool, hasWateringCanEnchantment;
        public int FacingDirection;
        public Vector2 lastClick;
        public float Stamina = 100;
        public int FarmingLevel;
        public float jitterStrength;
        public int ChargeSteps;
        public float LowestStamina = float.MaxValue;
        /// <summary>Like Farmer.toolPowerIncrease: one charge level (the real one also plays sound and effects).</summary>
        public void toolPowerIncrease() { this.toolPower.Value++; this.ChargeSteps++; }
        public void Halt() { }
        public void stopJittering() { }
        public void checkForExhaustion(float stamina) { }
        public static void canMoveNow(Farmer who) { who.UsingTool = false; who.CanMove = true; }
    }
    public record HUDMessage(string Text, int Type);
    public static class Game1
    {
        public static Farmer player = new();
        public static GameLocation currentLocation = new();
        public static object viewport = new();
        public static object? activeClickableMenu;
        public static int tileSize = 64;
        public static bool TimePasses = true;
        public static List<HUDMessage> Messages = new();
        public static bool shouldTimePass() => TimePasses;
        public static void addHUDMessage(HUDMessage message) => Messages.Add(message);
    }
}
namespace StardewValley.TerrainFeatures
{
    public class TerrainFeature { }
    public class HoeDirt : TerrainFeature
    {
        public NetValue<int> state = new(0);
        public Crop? crop = new();
    }
}
namespace StardewValley.Tools
{
    using Microsoft.Xna.Framework;
    public class WateringCan
    {
        public int WaterLeft = 40, waterCanMax = 40;
        public bool IsBottomless, IsEfficient, Reaching;
        public int UpgradeLevel;
        public float AnimationSpeedModifier = 1f;
        /// <summary>Origin tile of every DoFunction call (watering or refill), and its charge level.</summary>
        public List<Point> Uses = new();
        public List<int> UsePowers = new();
        public int LowestWater = int.MaxValue;
        public bool hasEnchantmentOfType<T>() => typeof(T) == typeof(StardewValley.Enchantments.ReachingToolEnchantment) && this.Reaching;
        public void Update(int direction, int unused, Farmer player) { }
        public void endUsing(GameLocation location, Farmer player) { }

        /// <summary>Same rules as WateringCan.DoFunction in 1.6.15 (see docs/game-mechanics.md).</summary>
        public void DoFunction(GameLocation location, int x, int y, int power, Farmer player)
        {
            power = player.toolPower.Value;
            Point tile = new(x / 64, y / 64);
            this.Uses.Add(tile);
            this.UsePowers.Add(power);
            if (location.CanRefillWateringCanOnTile(tile.X, tile.Y)) this.WaterLeft = this.waterCanMax;
            else if (this.WaterLeft > 0 || player.hasWateringCanEnchantment)
            {
                if (!this.IsEfficient) player.Stamina -= 2 * (power + 1) - player.FarmingLevel * 0.1f;
                player.LowestStamina = Math.Min(player.LowestStamina, player.Stamina);
                foreach (Point t in GameShapes.TilesAffected(tile, power, player.FacingDirection))
                {
                    if (!location.IgnoreWatering && !location.IgnoreTiles.Contains(t)
                        && location.terrainFeatures.GetValueOrDefault(new(t.X, t.Y)) is TerrainFeatures.HoeDirt dirt)
                        dirt.state.Value = 1;
                }
                if (!this.IsBottomless) this.WaterLeft -= power + 1;
                this.LowestWater = Math.Min(this.LowestWater, this.WaterLeft);
            }
        }
    }

    /// <summary>Transcription of Tool.tilesAffected from the decompiled 1.6.15 game, kept separate from the mod's own patterns.</summary>
    public static class GameShapes
    {
        public static List<Point> TilesAffected(Point tileLocation, int power, int facing)
        {
            power++;
            var list = new List<Point> { tileLocation };
            Point vector = new(0, 0);
            Point P(int dx, int dy) => new(tileLocation.X + dx, tileLocation.Y + dy);
            // Unit vectors per facing: forward (fx, fy) and the sideways offsets the game uses.
            (int fx, int fy) = facing switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };
            if (power >= 6) vector = P(fx * 2, fy * 2);
            else
            {
                if (power >= 2) { list.Add(P(fx, fy)); list.Add(P(fx * 2, fy * 2)); }
                if (power >= 3) { list.Add(P(fx * 3, fy * 3)); list.Add(P(fx * 4, fy * 4)); }
                if (power >= 4)
                {
                    list.RemoveAt(list.Count - 1);
                    list.RemoveAt(list.Count - 1);
                    (int sx, int sy) = facing is 0 or 2 ? (1, 0) : (0, 1);
                    for (int k = 2; k >= 0; k--) list.Add(P(sx + fx * k, sy + fy * k));
                    for (int k = 2; k >= 0; k--) list.Add(P(-sx + fx * k, -sy + fy * k));
                }
                if (power >= 5)
                    for (int i = list.Count - 1; i >= 0; i--) list.Add(new(list[i].X + fx * 3, list[i].Y + fy * 3));
            }
            if (power >= 6)
            {
                list.Clear();
                for (int i = vector.X - 2; i <= vector.X + 2; i++)
                    for (int j = vector.Y - 2; j <= vector.Y + 2; j++)
                        list.Add(new(i, j));
            }
            return list;
        }
    }
}
namespace StardewValley.Enchantments
{
    public class ReachingToolEnchantment { }
}
namespace StardewValley.Pathfinding
{
    using Microsoft.Xna.Framework;
    public class PathFindController
    {
        public Stack<Point>? pathToEndPoint;
        public Point Destination;
        public PathFindController(Farmer player, GameLocation location, Point destination, int facing)
        {
            this.Destination = destination;
            this.pathToEndPoint = location.FailPaths ? null : new(new[] { destination });
        }
    }
}
