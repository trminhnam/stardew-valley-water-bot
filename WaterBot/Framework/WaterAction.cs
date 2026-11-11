using Microsoft.Xna.Framework;

namespace WaterBot.Framework
{
    /// <summary>One use of the watering can: where to stand, which way to face, how far to charge, and what it waters.</summary>
    class WaterAction
    {
        public enum Kind { Water, Refill }

        public Kind Action;

        /// <summary>Tile the farmer stands on.</summary>
        public Point Stand;

        /// <summary>Tile passed to the game as the target: the pattern's first tile, or the single tile to water/refill from.</summary>
        public Point Origin;

        /// <summary>Facing direction (0 up, 1 right, 2 down, 3 left), or -1 to keep the current facing (watering the tile underfoot).</summary>
        public int Facing;

        /// <summary>Charge level (0 = single tile).</summary>
        public int Power;

        /// <summary>Crops this action is meant to water.</summary>
        public List<Point> Targets = new();

        public static WaterAction Single(Point stand, Point target) => new()
        {
            Action = Kind.Water,
            Stand = stand,
            Origin = target,
            Facing = FacingTowards(stand, target),
            Power = 0,
            Targets = new() { target },
        };

        public static WaterAction Refill(Point stand, Point water) => new()
        {
            Action = Kind.Refill,
            Stand = stand,
            Origin = water,
            Facing = FacingTowards(stand, water),
            Power = 0,
        };

        /// <summary>Direction to face to use the can on a tile within reach (up/down first for diagonals); -1 for the tile underfoot.</summary>
        public static int FacingTowards(Point from, Point to)
        {
            if (to.Y < from.Y) return 0;
            if (to.Y > from.Y) return 2;
            if (to.X < from.X) return 3;
            if (to.X > from.X) return 1;
            return -1;
        }

        public override string ToString() => $"{this.Action} p{this.Power} from {this.Stand} facing {this.Facing} at {this.Origin} ({this.Targets.Count} crops)";
    }
}
