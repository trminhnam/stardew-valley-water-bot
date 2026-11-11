using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Tools;

namespace WaterBot.Framework
{
    /// <summary>
    /// What the current watering can can do, read live from the game.
    /// The rules are confirmed against Stardew Valley 1.6.15; see docs/game-mechanics.md.
    /// </summary>
    class CanProfile
    {
        /// <summary>Highest charge level in the game (Iridium can with the Reaching enchantment).</summary>
        public const int MaxSupportedPower = 5;

        /// <summary>Length of the watering animation (75 + 100 + 500 ms) in ticks at 60 ticks per second.</summary>
        public const int AnimationTicks = 41;

        /// <summary>Game time per charge level (Game1.toolHoldPerPowerupLevel).</summary>
        public const float ChargeMillisecondsPerLevel = 600f;

        public int MaxPower;
        public bool Unlimited;
        public bool Efficient;
        public int FarmingLevel;
        public int ChargeTicksPerLevel = 36;

        /// <summary>Whether the planner may use charged (multi-tile) watering.</summary>
        public bool AllowCharged = true;

        public static CanProfile Read(Farmer farmer, WateringCan can, bool allowCharged)
        {
            int reaching = can.hasEnchantmentOfType<ReachingToolEnchantment>() ? 1 : 0;
            return new CanProfile
            {
                MaxPower = Math.Clamp(can.UpgradeLevel + reaching, 0, MaxSupportedPower),
                Unlimited = can.IsBottomless || farmer.hasWateringCanEnchantment,
                Efficient = can.IsEfficient,
                FarmingLevel = farmer.FarmingLevel,
                ChargeTicksPerLevel = Math.Max(1, (int)Math.Ceiling(ChargeMillisecondsPerLevel * can.AnimationSpeedModifier * 60f / 1000f)),
                AllowCharged = allowCharged && PatternsVerified,
            };
        }

        /// <summary>Highest charge level the planner may use.</summary>
        public int UsablePower => this.AllowCharged ? this.MaxPower : 0;

        /// <summary>Stamina cost of watering at this charge level, rounded up to a whole number.</summary>
        public int Cost(int power) => CostUp(power, this.FarmingLevel, this.Efficient);

        /// <summary>Water used by one watering at this charge level.</summary>
        public int WaterUse(int power) => this.Unlimited ? 0 : power + 1;

        public bool CanAfford(float stamina, int power) => CanAffordCost(stamina, this.Cost(power));

        /// <summary>Estimated ticks for one watering: charging (if not instant) plus the animation.</summary>
        public int ActionTicks(int power, bool instantCharge) => AnimationTicks + (instantCharge ? 0 : power * this.ChargeTicksPerLevel);

        /// <summary>
        /// The game charges 2 × (power + 1) − 0.1 × farming level (nothing with the Efficient enchantment).
        /// This is that cost rounded up, computed in tenths so float error can't round 1.0 up to 2.
        /// </summary>
        public static int CostUp(int power, int farmingLevel, bool efficient)
        {
            if (efficient) return 0;
            int tenths = 20 * (power + 1) - farmingLevel;
            return tenths <= 0 ? 0 : (tenths + 9) / 10;
        }

        /// <summary>
        /// Only water if, with stamina rounded down (as the energy bar shows it), at least 1 whole point
        /// is left afterwards. This never lets the farmer reach 0.
        /// </summary>
        public static bool CanAffordCost(float stamina, int cost) => (int)Math.Floor(stamina) - cost >= 1;

        public static Point Forward(int direction) => direction switch
        {
            0 => new Point(0, -1),
            1 => new Point(1, 0),
            2 => new Point(0, 1),
            _ => new Point(-1, 0),
        };

        private static readonly Point[][][] patterns = buildPatterns();

        /// <summary>Tiles watered at this charge level, relative to the origin (the tile in front of the farmer).</summary>
        public static Point[] Pattern(int power, int direction) => patterns[power][direction];

        private static Point[][][] buildPatterns()
        {
            // (forward, sideways) ranges per power, matching Tool.tilesAffected.
            var shapes = new (int F0, int F1, int S0, int S1)[]
            {
                (0, 0, 0, 0), // 0: single tile
                (0, 2, 0, 0), // 1: 1×3 line
                (0, 4, 0, 0), // 2: 1×5 line
                (0, 2, -1, 1), // 3: 3×3
                (0, 5, -1, 1), // 4: 3 wide × 6 deep
                (0, 4, -2, 2), // 5: 5×5, centred 2 ahead
            };
            var result = new Point[shapes.Length][][];
            for (int p = 0; p < shapes.Length; p++)
            {
                result[p] = new Point[4][];
                for (int d = 0; d < 4; d++)
                {
                    Point f = Forward(d);
                    Point s = new(-f.Y, f.X);
                    var list = new List<Point>();
                    for (int a = shapes[p].F0; a <= shapes[p].F1; a++)
                        for (int b = shapes[p].S0; b <= shapes[p].S1; b++)
                            list.Add(new Point(f.X * a + s.X * b, f.Y * a + s.Y * b));
                    result[p][d] = list.ToArray();
                }
            }
            return result;
        }

        /// <summary>False if the self-check found the game's patterns differ; charged watering is then disabled.</summary>
        public static bool PatternsVerified = true;

        /// <summary>
        /// Compares the patterns above with the game's own Tool.tilesAffected for the farmer's current facing.
        /// Returns a description of the first difference, or null if they all match.
        /// </summary>
        public static string? VerifyPatterns(Func<Point, int, IEnumerable<Point>> gameTiles, int facing)
        {
            Point origin = new(50, 50);
            for (int p = 0; p <= MaxSupportedPower; p++)
            {
                var expected = Pattern(p, facing).Select(o => new Point(origin.X + o.X, origin.Y + o.Y)).ToHashSet();
                var actual = gameTiles(origin, p).ToHashSet();
                if (!expected.SetEquals(actual))
                    return $"power {p}, facing {facing}: expected {expected.Count} tiles, game has {actual.Count}";
            }
            return null;
        }
    }
}
