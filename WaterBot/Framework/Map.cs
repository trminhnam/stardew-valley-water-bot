using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace WaterBot.Framework
{
    public delegate void console(string message);

    /// <summary>
    /// Contains map data, walking distances and refill search. Planning lives in Planner.
    /// </summary>
    class Map
    {
        public List<List<Tile>> map;

        public List<Tile> waterableTiles;

        public int width;

        public int height;

        private readonly (int Y, int X)[] directions = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private readonly (int Y, int X)[] diagonals = { (1, 1), (-1, -1), (1, -1), (-1, 1) };

        /// <summary>Walking distance from the player's start tile; -1 where unreachable.</summary>
        private int[] reachable = Array.Empty<int>();

        public Map()
        {
            this.map = new List<List<Tile>>();
            this.waterableTiles = new List<Tile>();
        }

        /// <summary>
        /// Whether tile is blocked (the name is historical: true means the farmer collides).
        /// </summary>
        ///
        /// <param name="x">Row (Y) of tile.</param>
        /// <param name="y">Column (X) of tile.</param>
        public static bool tileIsPassable(int x, int y)
        {
            return Game1.currentLocation.isCollidingPosition(new Rectangle(y * 64 + 1, x * 64 + 1, 62, 62), Game1.viewport, isFarmer: true, -1, glider: false, Game1.player);
        }

        /// <summary>
        /// Whether tile can refill watering can.
        /// </summary>
        ///
        /// <param name="x">Row (Y) of tile.</param>
        /// <param name="y">Column (X) of tile.</param>
        public static bool tileIsRefillable(int x, int y)
        {
            return Game1.currentLocation.CanRefillWateringCanOnTile(y, x);
        }

        /// <summary>
        /// Whether tile needs watering.
        /// </summary>
        ///
        /// <param name="x">X of tile.</param>
        /// <param name="y">Y of tile.</param>
        public static bool tileNeedsWatering(int x, int y)
        {
            Vector2 index = new Vector2(x, y);

            if (!Game1.currentLocation.isTileHoeDirt(index))
                return false;

            if (!Game1.currentLocation.terrainFeatures.TryGetValue(index, out TerrainFeature? feature))
                return false;

            if (feature is HoeDirt hoeDirt)
            {
                if (hoeDirt.state.Value != 0)
                    return false;

                Crop? crop = hoeDirt.crop;
                if (crop == null || crop.dead.Value)
                    return false;

                return (crop.fullyGrown.Value && crop.dayOfCurrentPhase.Value > 0) ||
                       (crop.currentPhase.Value < crop.phaseDays.Count - 1) ||
                       crop.RegrowsAfterHarvest();
            }

            return false;
        }

        /// <summary>
        /// Loads the current location into the tile grid and finds every tile reachable from the player.
        /// </summary>
        public void loadMap()
        {
            this.height = Game1.currentLocation.map.Layers[0].LayerHeight;
            this.width = Game1.currentLocation.map.Layers[0].LayerWidth;

            List<List<Tile>> map = new List<List<Tile>>();
            List<Tile> waterableTiles = new List<Tile>();

            for (int y = 0; y < this.height; y++)
            {
                List<Tile> row = new List<Tile>();

                for (int x = 0; x < this.width; x++)
                {
                    bool needsWatering = Map.tileNeedsWatering(x, y);

                    Tile tile = new Tile(
                        x,
                        y,
                        Map.tileIsPassable(y, x),
                        Map.tileIsRefillable(y, x),
                        needsWatering
                    );

                    if (needsWatering)
                    {
                        waterableTiles.Add(tile);
                    }
                    row.Add(tile);
                }

                map.Add(row);
            }

            this.map = map;
            this.waterableTiles = waterableTiles;
            this.reachable = this.walkingDistances(Game1.player.TilePoint);
        }

        public bool inBounds(Point point) => point.X >= 0 && point.Y >= 0 && point.X < this.width && point.Y < this.height;

        public int index(Point point) => point.Y * this.width + point.X;

        public Tile at(Point point) => this.map[point.Y][point.X];

        /// <summary>Whether the farmer can stand on this tile and walk to it from where the run started.</summary>
        public bool isReachable(Point point) => this.inBounds(point)
            && !this.map[point.Y][point.X].block && this.reachable[this.index(point)] >= 0;

        /// <summary>
        /// Breadth-first search over walkable tiles. Every step costs the same, so this gives
        /// the exact walking distance (in tiles) from start to every tile; -1 where unreachable.
        /// </summary>
        public int[] walkingDistances(Point start)
        {
            int[] distance = new int[this.width * this.height];
            Array.Fill(distance, -1);
            if (!this.inBounds(start)) return distance;

            Queue<Point> queue = new();
            distance[this.index(start)] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();
                int next = distance[this.index(current)] + 1;
                foreach (var (dy, dx) in this.directions)
                {
                    Point neighbor = new(current.X + dx, current.Y + dy);
                    if (!this.inBounds(neighbor) || this.map[neighbor.Y][neighbor.X].block
                        || distance[this.index(neighbor)] >= 0)
                        continue;
                    distance[this.index(neighbor)] = next;
                    queue.Enqueue(neighbor);
                }
            }
            return distance;
        }

        /// <summary>Reachable tiles the farmer can water the target from: the tile itself and its 8 neighbors.</summary>
        public IEnumerable<Tile> standingPlaces(Tile target)
        {
            if (this.isReachable(target.getPoint())) yield return target;
            foreach (var (dy, dx) in this.directions.Concat(this.diagonals))
            {
                Point point = new(target.x + dx, target.y + dy);
                if (this.isReachable(point)) yield return this.map[point.Y][point.X];
            }
        }

        /// <summary>
        /// Finds the nearest refill spot by walking distance. Water tiles are usually blocked,
        /// so the farmer stands on the walkable tile next to the water.
        /// </summary>
        public WaterAction? getClosestRefill(Tile start, console console)
        {
            bool[] seen = new bool[this.width * this.height];
            Queue<(Tile Current, Tile? Last)> queue = new();
            queue.Enqueue((start, null));
            seen[this.index(start.getPoint())] = true;

            while (queue.Count > 0)
            {
                var (current, last) = queue.Dequeue();

                if (current.water)
                {
                    Point stand = current.block ? (last ?? start).getPoint() : current.getPoint();
                    return WaterAction.Refill(stand, current.getPoint());
                }

                if (current.block && current != start)
                {
                    continue;
                }

                foreach (var (dy, dx) in this.directions)
                {
                    Point point = new(current.x + dx, current.y + dy);
                    if (!this.inBounds(point) || seen[this.index(point)]) continue;
                    Tile cell = this.map[point.Y][point.X];
                    if (!cell.block || cell.water)
                    {
                        seen[this.index(point)] = true;
                        queue.Enqueue((cell, current));
                    }
                }
            }

            return null;
        }
    }
}
