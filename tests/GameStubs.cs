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
            public bool UseSmallGrouping { get; set; }
            public bool RefillOnFinish { get; set; }
            public int RefillIfLower { get; set; } = 95;
            public bool RedoPathOnRefill { get; set; }
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
        public void SetOwner(Farmer farmer) { }
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
        public bool IsBottomless;
        public List<Point> Uses = new();
        public void Update(int direction, int unused, Farmer player) { }
        public void endUsing(GameLocation location, Farmer player) { }
        public void DoFunction(GameLocation location, int x, int y, int power, Farmer player)
        {
            Point tile = new(x / 64, y / 64);
            this.Uses.Add(tile);
            if (location.CanRefillWateringCanOnTile(tile.X, tile.Y)) this.WaterLeft = this.waterCanMax;
            else if (this.WaterLeft > 0 || player.hasWateringCanEnchantment)
            {
                if (!this.IsBottomless) this.WaterLeft--;
                player.Stamina -= 2;
                if (!location.IgnoreWatering && location.terrainFeatures.GetValueOrDefault(new(tile.X, tile.Y)) is TerrainFeatures.HoeDirt dirt)
                    dirt.state.Value = 1;
            }
        }
    }
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
