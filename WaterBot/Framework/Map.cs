using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace WaterBot.Framework
{
    public delegate void console(string message);

    /// <summary>
    /// Contains map data and relevant path finding / grouping algorithms.
    /// </summary>
    class Map
    {
        public List<List<Tile>> map;

        public List<Tile> waterableTiles;

        public int width;

        public int height;

        private (int Y, int X, Func<int, int, bool> IsInBounds)[] directions;

        private (int Y, int X, Func<int, int, bool> IsInBounds)[] diagonals;

        private readonly HashSet<Tile> reachable = new();

        public Map()
        {
            this.map = new List<List<Tile>>();
            this.waterableTiles = new List<Tile>();
            this.directions = new (int Y, int X, Func<int, int, bool> IsInBounds)[] {
                new (1, 0, (int y, int x) => y < this.height),
                new (-1, 0, (int y, int x) => y >= 0),
                new (0, 1, (int y, int x) => x < this.width),
                new (0, -1, (int y, int x) => x >= 0),
            };
            this.diagonals = new (int Y, int X, Func<int, int, bool> IsInBounds)[] {
                new (1, 1, (int y, int x) => x < this.width && y < this.height),
                new (-1, -1, (int y, int x) => x >= 0 && y >= 0),
                new (1, -1, (int y, int x) => x >= 0 && y < this.height),
                new (-1, 1, (int y, int x) => x < this.width && y >= 0),
            };
        }

        /// <summary>
        /// Converts coords to tile.
        /// </summary>
        /// 
        /// <param name="x">X of tile.</param>
        /// <param name="y">Y of tile.</param>
        /// <param name="width">Number of tiles horizontally.</param>
        /// <param name="height">Number of tiles vertically.</param>
        public static Point convertCoords(int x, int y, int width, int height)
        {
            return new Point(y, x);
        }

        /// <summary>
        /// Whether tile is passable.
        /// </summary>
        /// 
        /// <param name="x">X of tile.</param>
        /// <param name="y">Y of tile.</param>
        public static bool tileIsPassable(int x, int y)
        {
            return Game1.currentLocation.isCollidingPosition(new Rectangle(y * 64 + 1, x * 64 + 1, 62, 62), Game1.viewport, isFarmer: true, -1, glider: false, Game1.player);
        }

        /// <summary>
        /// Whether tile can refill watering can.
        /// </summary>
        /// 
        /// <param name="x">X of tile.</param>
        /// <param name="y">Y of tile.</param>
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
        /// Loads farm data into array.
        /// </summary>
        /// 
        /// <param name="location">Farm location instance.</param>
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
                    Point converted = Map.convertCoords(x, y, this.width, this.height);
                    bool needsWatering = Map.tileNeedsWatering(converted.Y, converted.X);

                    Tile tile = new Tile(
                        x,
                        y,
                        Map.tileIsPassable(converted.X, converted.Y),
                        Map.tileIsRefillable(converted.X, converted.Y),
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
            this.reachable.Clear();
            Point start = Game1.player.TilePoint;
            if (!this.inBounds(start)) return;
            Queue<Tile> queue = new();
            Tile startTile = this.map[start.Y][start.X];
            this.reachable.Add(startTile);
            queue.Enqueue(startTile);
            while (queue.Count > 0)
            {
                foreach (Tile neighbor in this.getNeighbors(queue.Dequeue(), true))
                    if (this.reachable.Add(neighbor)) queue.Enqueue(neighbor);
            }
        }

        /// <summary>
        /// Returns a list of adjacent tiles.
        /// </summary>
        /// 
        /// <param name="tile">Tile to find neighbors of.</param>
        public List<Tile> getNeighbors(Tile tile, bool checkBlock = false, bool checkWaterable = false)
        {
            List<Tile> list = new List<Tile>();

            if (tile.x > 0)
            {
                Tile neighbor = this.map[tile.y][tile.x - 1];
                if ((!checkBlock || !neighbor.block) && (!checkWaterable || neighbor.waterable))
                {
                    list.Add(neighbor);
                }
            }
            if (tile.y > 0)
            {
                Tile neighbor = this.map[tile.y - 1][tile.x];
                if ((!checkBlock || !neighbor.block) && (!checkWaterable || neighbor.waterable))
                {
                    list.Add(neighbor);
                }
            }
            if (tile.x < this.width - 1)
            {
                Tile neighbor = this.map[tile.y][tile.x + 1];
                if ((!checkBlock || !neighbor.block) && (!checkWaterable || neighbor.waterable))
                {
                    list.Add(neighbor);
                }
            }
            if (tile.y < this.height - 1)
            {
                Tile neighbor = this.map[tile.y + 1][tile.x];
                if ((!checkBlock || !neighbor.block) && (!checkWaterable || neighbor.waterable))
                {
                    list.Add(neighbor);
                }
            }

            return list;
        }

        public bool inBounds(Point point) => point.X >= 0 && point.Y >= 0 && point.X < this.width && point.Y < this.height;

        public bool isReachable(Point point) => this.inBounds(point)
            && !this.map[point.Y][point.X].block && this.reachable.Contains(this.map[point.Y][point.X]);

        private IEnumerable<Tile> standingPlaces(Tile target)
        {
            if (this.isReachable(target.getPoint())) yield return target;
            foreach (var direction in this.directions.Concat(this.diagonals))
            {
                Point point = new(target.x + direction.X, target.y + direction.Y);
                if (this.isReachable(point)) yield return this.map[point.Y][point.X];
            }
        }

        public Point? groupStandingPlace(Group group)
        {
            if (group.center != null && this.isReachable(group.center.getPoint()))
                return group.center.getPoint();
            if (group.Count() == 0) return null;
            Point center = new((int)group.getList().Average(t => t.x), (int)group.getList().Average(t => t.y));
            return group.getList().SelectMany(this.standingPlaces).Distinct()
                .OrderBy(t => t.distanceTo(center)).Select(t => (Point?)t.getPoint()).FirstOrDefault();
        }

        /// <summary>Choose reachable standing positions which cover all accessible targets.</summary>
        public List<Group> getMinimalCoverGroups()
        {
            Group all = new(0);
            foreach (Tile tile in this.waterableTiles) all.Add(tile);
            List<ActionableTile> stops = this.findFillPath(all, _ => { });
            List<Group> groups = new();
            foreach (ActionableTile stop in stops)
            {
                List<Tile> targets = new();
                while (!stop.isDone())
                {
                    Point target = stop.Pop();
                    targets.Add(this.map[target.Y][target.X]);
                }
                Point stand = stop.getStand();
                groups.Add(new Group(groups.Count, this.map[stand.Y][stand.X], targets));
            }
            // findFillPath uses these as scheduling flags; the controller plans each group later.
            foreach (Tile tile in this.waterableTiles) tile.watered = false;
            return groups;
        }

        /// <summary>Find side-connected crops, without recursion or special edge indexing.</summary>
        public List<Group> findGroupings(console console)
        {
            List<Group> groups = new();
            HashSet<Tile> visited = new();
            foreach (Tile tile in this.waterableTiles)
            {
                if (!visited.Add(tile)) continue;
                Group group = new(groups.Count);
                Stack<Tile> stack = new();
                stack.Push(tile);
                while (stack.Count > 0)
                {
                    Tile current = stack.Pop();
                    group.Add(current);
                    foreach (Tile neighbor in this.getNeighbors(current, checkWaterable: true))
                        if (visited.Add(neighbor)) stack.Push(neighbor);
                }
                groups.Add(group);
            }
            return groups;
        }

        public Tile findClosestWalkableTile(Tile center)
        {
            foreach (List<Tile> row in this.map)
            {
                foreach (Tile tile in row)
                {
                    tile.walkableCheck = false;
                }
            }

            List<Tile> queue = new() { center };

            while (queue.Count > 0)
            {
                Tile current = queue[0];
                queue.RemoveAt(0);

                if (current.walkableCheck)
                {
                    continue;
                }

                current.walkableCheck = true;

                if (!current.block)
                {
                    return current;
                } else
                {
                    foreach (var direction in this.directions.Concat(this.diagonals))
                    {
                        if (direction.IsInBounds(current.y + direction.Y, current.x + direction.X))
                        {
                            if (!this.map[current.y + direction.Y][current.x + direction.X].walkableCheck)
                            {
                                queue.Add(this.map[current.y + direction.Y][current.x + direction.X]);
                            }
                        }
                    }
                }
            }

            throw new InvalidOperationException("No refill location found.");
        }

        /// <summary>
        /// Runs A* on map to find shortest path from start to end.
        /// </summary>
        /// 
        /// <param name="console">Function for printing to debug console.</param>
        /// <param name="start">Point to start at.</param>
        /// <param name="end">Point to end at.</param>
        public Tuple<List<Tile>, int> walkablePathBetweenPoints(console console, Point start, Point end)
        {
            List<Tile> openSet = new List<Tile>();
            Dictionary<Tile, Tile> cameFrom = new Dictionary<Tile, Tile>();
            Dictionary<Tile, int> gScore = new Dictionary<Tile, int>();
            Dictionary<Tile, int> fScore = new Dictionary<Tile, int>();

            Tile startTile = this.map[start.Y][start.X];

            openSet.Add(startTile);

            gScore.Add(startTile, 0);
            fScore.Add(startTile, startTile.distanceTo(end));

            while (openSet.Count > 0)
            {
                Tile current = openSet[0];
                int smallest = int.MaxValue;

                foreach (Tile tile in openSet)
                {
                    if (fScore[tile] < smallest)
                    {
                        smallest = fScore[tile];
                        current = tile;
                    }
                }

                if (current.Equals(end))
                {
                    List<Tile> path = new List<Tile>();
                    while (cameFrom.Keys.Contains(current))
                    {
                        current = cameFrom[current];
                        path.Add(current);
                    }
                    return new Tuple<List<Tile>, int>(path, path.Count);
                }

                openSet.Remove(current);

                List<Tile> neighbors = getNeighbors(current, true, false);

                foreach (Tile neighbor in neighbors)
                {
                    if (!gScore.Keys.Contains(neighbor))
                    {
                        gScore.Add(neighbor, int.MaxValue);
                    }

                    int tenativeGScore = gScore[current] + 1;
                    if (tenativeGScore < gScore[neighbor])
                    {
                        if (!cameFrom.Keys.Contains(neighbor))
                        {
                            cameFrom.Add(neighbor, current);
                        }
                        if (!fScore.Keys.Contains(neighbor))
                        {
                            fScore.Add(neighbor, int.MaxValue);
                        }

                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tenativeGScore;
                        fScore[neighbor] = tenativeGScore + neighbor.distanceTo(end);

                        if (!openSet.Contains(neighbor))
                        {
                            openSet.Add(neighbor);
                        }
                    }
                }
            }
            return new Tuple<List<Tile>, int>(new List<Tile>(), int.MaxValue);
        }

        /// <summary>
        /// Uses A* to find cost between different groupings.
        /// </summary>
        /// 
        /// <param name="console">Function for printing to debug console.</param>
        public int[,] generateCostMatrix(console console, List<Group> groupings)
        {
            groupings.RemoveAll(group => group.Count() == 0 || this.groupStandingPlace(group) == null);

            int nodes = groupings.Count + 1;

            int[,] costMatrix = new int[nodes, nodes];

            // From
            for (int i = 0; i < nodes; i++)
            {
                // To
                for (int j = 0; j < nodes; j++)
                {
                    if (i == j)
                    {
                        costMatrix[i, j] = -1;
                    }
                    else
                    {
                        if (costMatrix[j, i] > 0)
                        {
                            costMatrix[i, j] = costMatrix[j, i];
                        }
                        else if (j == 0 || i == 0)
                        {
                            Point start = new Point(Game1.player.TilePoint.X, Game1.player.TilePoint.Y);
                            Point end = groupings[i == 0 ? j - 1 : i - 1].Centroid(this);

                            Tuple<List<Tile>, int> path = this.walkablePathBetweenPoints(console, start, end);

                            costMatrix[i, j] = path.Item2;
                        }
                        else
                        {
                            Point start = groupings[i - 1].Centroid(this);
                            Point end = groupings[j - 1].Centroid(this);

                            Tuple<List<Tile>, int> path = this.walkablePathBetweenPoints(console, start, end);

                            costMatrix[i, j] = path.Item2;
                        }
                    }
                }
            }

            List<int> safeGroups = new List<int>();
            List<int> deleteGroups = new List<int>();

            for (int i = 0; i < nodes; i++)
            {
                if (costMatrix[0, i] == int.MaxValue)
                {
                    deleteGroups.Add(i);
                }
                else
                {
                    safeGroups.Add(i);
                }
            }

            deleteGroups.Sort();

            for (int i = deleteGroups.Count - 1; i >= 0; i--)
            {
                groupings.RemoveAt(deleteGroups[i] - 1);
            }

            int[,] reachableCostMatrix = new int[safeGroups.Count, safeGroups.Count];

            for (int i = 0; i < safeGroups.Count; i++)
            {
                for (int j = 0; j < safeGroups.Count; j++)
                {
                    reachableCostMatrix[i, j] = costMatrix[safeGroups[i], safeGroups[j]];
                    reachableCostMatrix[j, i] = costMatrix[safeGroups[j], safeGroups[i]];
                }
            }

            return reachableCostMatrix;
        }  

        /// <summary>
        /// Runs TSP Greedy to find best path through all groups
        /// </summary>
        /// 
        /// <param name="console">Function for printing to debug console.</param>
        public List<Group> findGroupPath(console console, List<Group> groupings)
        {
            try
            {
                List<Group> path = new List<Group>();

                int[,] costMatrix = this.generateCostMatrix(console, groupings);

                int counter = 0;
                int j = 0;
                int i = 0;
                int min = int.MaxValue;

                List<int> visitedRouteList = new() { 0 };

                int[] route = new int[costMatrix.Length];

                while (i < costMatrix.GetLength(0) && j < costMatrix.GetLength(1))
                {
                    if (counter >= costMatrix.GetLength(0) - 1)
                    {
                        break;
                    }

                    if (j != i && !(visitedRouteList.Contains(j)))
                    {
                        if (costMatrix[i, j] < min)
                        {
                            min = costMatrix[i, j];
                            route[counter] = j + 1;
                        }
                    }
                    j++;

                    if (j == costMatrix.GetLength(0))
                    {
                        min = int.MaxValue;
                        visitedRouteList.Add(route[counter] - 1);

                        j = 0;
                        i = route[counter] - 1;
                        counter++;
                    }
                }

                foreach (int index in visitedRouteList)
                {
                    if (index != 0)
                    {
                        path.Add(groupings[index - 1]);
                    }
                }

                return path;
            } catch (IndexOutOfRangeException)
            {
                console("WaterBot encountered an error and will return a sub-optimal path.");
                return groupings;
            } catch (Exception)
            {
                console("WaterBot encountered an unknown error and will return a sub-optimal path.");
                return groupings;
            }
        }

        /// <summary>Plan each target once, using only reachable standing positions.</summary>
        public List<ActionableTile> findFillPath(Group group, console console)
        {
            HashSet<Tile> pending = group.getList().Where(t => t.waterable && !t.watered).ToHashSet();
            List<ActionableTile> stops = new();
            if (group.center != null && this.isReachable(group.center.getPoint())
                && pending.All(t => Math.Abs(t.x - group.center.x) <= 1 && Math.Abs(t.y - group.center.y) <= 1))
            {
                if (pending.Count > 0)
                {
                    stops.Add(new ActionableTile(group.center.getPoint(), pending.Select(t => t.getPoint()).ToList(), ActionableTile.Action.Water));
                    foreach (Tile tile in pending) tile.watered = true;
                }
                return stops;
            }
            Point position = Game1.player.TilePoint;
            while (pending.Count > 0)
            {
                Tile target = pending.OrderBy(t => t.distanceTo(position)).First();
                Tile? stand = this.standingPlaces(target)
                    .OrderByDescending(candidate => pending.Count(t => Math.Abs(t.x - candidate.x) <= 1 && Math.Abs(t.y - candidate.y) <= 1))
                    .ThenBy(candidate => candidate.distanceTo(position)).FirstOrDefault();
                if (stand == null)
                {
                    console($"No reachable standing position for crop {target.getPoint()}. Skipping it.");
                    pending.Remove(target);
                    continue;
                }
                List<Tile> covered = pending.Where(t => Math.Abs(t.x - stand.x) <= 1 && Math.Abs(t.y - stand.y) <= 1).ToList();
                stops.Add(new ActionableTile(stand.getPoint(), covered.Select(t => t.getPoint()).ToList(), ActionableTile.Action.Water));
                foreach (Tile tile in covered)
                {
                    pending.Remove(tile);
                    tile.watered = true; // Scheduled, not confirmed watered. waterable tracks confirmed progress.
                }
                position = stand.getPoint();
            }
            return stops;
        }

        /// <summary>
        /// Finds the closest refill location
        /// </summary>
        public ActionableTile? getClosestRefill(Tile start, console console)
        {
            foreach (List<Tile> row in this.map)
            {
                foreach (Tile tile in row)
                {
                    tile.waterCheck = false;
                }
            }

            List<Tuple<Tile, Tile>> queue = new() { new (start, null!) }; // Current, Last

            Tile? current = null;
            Tile? last = null;

            while (queue.Count > 0)
            {
                last = queue[0].Item2;
                current = queue[0].Item1;
                queue.RemoveAt(0);

                if (current.waterCheck)
                {
                    continue;
                }

                current.waterCheck = true;

                if (current.water)
                {
                    List<Point> actions = new() { current.getPoint() };

                    if (current.block)
                    {
                        return new ActionableTile((last ?? start).getPoint(), actions, ActionableTile.Action.Refill);
                    }
                    else
                    {
                        return new ActionableTile(current.getPoint(), actions, ActionableTile.Action.Refill);
                    }
                }

                if (current.block && current != start)
                {
                    continue;
                }

                foreach (var direction in this.directions)
                {
                    if (direction.IsInBounds(current.y + direction.Y, current.x + direction.X))
                    {
                        var cell = this.map[current.y + direction.Y][current.x + direction.X];
                        if (!cell.waterCheck && (!cell.block || cell.water))
                        {
                            queue.Add(new Tuple<Tile, Tile>(cell, current));
                        }
                    }
                }
            }

            return null;
        }
    }
}
