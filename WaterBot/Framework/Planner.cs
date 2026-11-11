using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace WaterBot.Framework
{
    /// <summary>
    /// Plans a watering run in two steps:
    /// 1. choose which can uses to make (greedy set cover, best crops per cost first), then
    /// 2. order the standing tiles by real walking distance (nearest next, then 2-opt).
    /// </summary>
    static class Planner
    {
        public sealed class Options
        {
            /// <summary>Score by time per crop first instead of stamina per crop.</summary>
            public bool OptimizeSpeed;

            /// <summary>Charging is instant, so charge time doesn't count towards an action's time.</summary>
            public bool InstantCharge;

            /// <summary>Time budget for the 2-opt route improvement.</summary>
            public int TwoOptMilliseconds = 50;
        }

        private sealed class Candidate
        {
            public Point Stand;
            public Point Origin;
            public int Facing;
            public int Power;
            public int[] Tiles = Array.Empty<int>(); // tile indexes watered by this use
            public int StandIndex;
            public int WalkFromStart;
        }

        /// <summary>
        /// Plan watering for every crop that needs it, starting at <paramref name="start"/>.
        /// Only charge levels affordable at <paramref name="stamina"/> are used.
        /// Crops in <paramref name="skip"/> (already failed twice) and crops that can't be reached are left out.
        /// </summary>
        public static List<WaterAction> Plan(Map map, CanProfile can, float stamina, Options options, console log, Point start, ISet<Point>? skip = null)
        {
            var result = new List<WaterAction>();
            if (!can.CanAfford(stamina, 0)) return result;

            bool[] pending = new bool[map.width * map.height];
            var crops = new List<Tile>();
            foreach (Tile tile in map.waterableTiles)
            {
                if (!tile.waterable || skip?.Contains(tile.getPoint()) == true) continue;
                pending[map.index(tile.getPoint())] = true;
                crops.Add(tile);
            }
            if (crops.Count == 0) return result;

            int[] fromStart = map.walkingDistances(start);
            List<Candidate> candidates = listCandidates(map, can, stamina, crops, fromStart);

            // Crops nothing can reach are reported, not planned.
            var coverable = new bool[pending.Length];
            foreach (Candidate c in candidates) foreach (int i in c.Tiles) coverable[i] = true;
            foreach (Tile crop in crops)
            {
                int i = map.index(crop.getPoint());
                if (!coverable[i])
                {
                    log($"No reachable way to water crop {crop.getPoint()}. Skipping it.");
                    pending[i] = false;
                }
            }

            List<List<WaterAction>> stops = chooseActions(map, can, options, candidates, pending);
            List<List<WaterAction>> ordered = orderStops(map, stops, start, fromStart, options);
            foreach (var stop in ordered) result.AddRange(stop);
            return result;
        }

        /// <summary>Every way to water at least one crop: single tiles from any reachable neighbour, and charged patterns.</summary>
        private static List<Candidate> listCandidates(Map map, CanProfile can, float stamina, List<Tile> crops, int[] fromStart)
        {
            var list = new List<Candidate>();
            var seen = new HashSet<long>();

            // Single tiles (power 0): stand on a crop or next to one, and water every crop within reach
            // (the 8 neighbours and the tile underfoot), one tile per use.
            var isCrop = new bool[map.width * map.height];
            foreach (Tile crop in crops) isCrop[map.index(crop.getPoint())] = true;
            foreach (Tile crop in crops)
            {
                foreach (Tile stand in map.standingPlaces(crop))
                {
                    if (!seen.Add(map.index(stand.getPoint()))) continue;
                    var tiles = new List<int>();
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            Point t = new(stand.x + dx, stand.y + dy);
                            if (map.inBounds(t) && isCrop[map.index(t)]) tiles.Add(map.index(t));
                        }
                    list.Add(new Candidate
                    {
                        Stand = stand.getPoint(),
                        Origin = stand.getPoint(),
                        Facing = -1,
                        Power = 0,
                        Tiles = tiles.ToArray(),
                        StandIndex = map.index(stand.getPoint()),
                        WalkFromStart = fromStart[map.index(stand.getPoint())],
                    });
                }
            }

            // Charged patterns: for each crop, every placement of every affordable pattern that includes it.
            seen.Clear();
            for (int power = 1; power <= can.UsablePower; power++)
            {
                if (!can.CanAfford(stamina, power)) break; // costs only rise with power
                for (int dir = 0; dir < 4; dir++)
                {
                    Point forward = CanProfile.Forward(dir);
                    Point[] pattern = CanProfile.Pattern(power, dir);
                    foreach (Tile crop in crops)
                    {
                        foreach (Point offset in pattern)
                        {
                            Point origin = new(crop.x - offset.X, crop.y - offset.Y);
                            Point stand = new(origin.X - forward.X, origin.Y - forward.Y);
                            if (!map.inBounds(origin) || !map.isReachable(stand)) continue;
                            // A pattern starting on water refills the can instead of watering (WateringCan.DoFunction).
                            if (map.at(origin).water) continue;
                            long key = ((long)map.index(stand) << 8) | (uint)(dir << 4 | power);
                            if (!seen.Add(key)) continue;
                            var tiles = new List<int>();
                            foreach (Point o in pattern)
                            {
                                Point t = new(origin.X + o.X, origin.Y + o.Y);
                                if (map.inBounds(t)) tiles.Add(map.index(t));
                            }
                            list.Add(new Candidate
                            {
                                Stand = stand,
                                Origin = origin,
                                Facing = dir,
                                Power = power,
                                Tiles = tiles.ToArray(),
                                StandIndex = map.index(stand),
                                WalkFromStart = fromStart[map.index(stand)],
                            });
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>Higher is better. Compared field by field.</summary>
        private readonly record struct Score(double A, double B, double C, double D, double E) : IComparable<Score>
        {
            public int CompareTo(Score other)
            {
                int c = this.A.CompareTo(other.A);
                if (c != 0) return c;
                c = this.B.CompareTo(other.B);
                if (c != 0) return c;
                c = this.C.CompareTo(other.C);
                if (c != 0) return c;
                c = this.D.CompareTo(other.D);
                return c != 0 ? c : this.E.CompareTo(other.E);
            }
        }

        /// <summary>
        /// Greedy weighted set cover with lazy re-scoring: repeatedly take the use (or single-tile stop)
        /// that waters the most still-dry crops per stamina point (ties: less water, less time,
        /// more crops from one spot, shorter walk from the start).
        /// A charged use never waters the tile underfoot, so if the farmer would stand on a dry crop,
        /// a single-tile watering of that crop is added at the same spot and counted in the cost.
        /// </summary>
        private static List<List<WaterAction>> chooseActions(Map map, CanProfile can, Options options, List<Candidate> candidates, bool[] pending)
        {
            var stops = new List<List<WaterAction>>();
            var queue = new PriorityQueue<int, Score>(Comparer<Score>.Create((a, b) => b.CompareTo(a)));
            for (int i = 0; i < candidates.Count; i++)
            {
                Score? s = score(candidates[i], can, options, pending);
                if (s != null) queue.Enqueue(i, s.Value);
            }

            while (queue.TryDequeue(out int index, out Score queued))
            {
                Candidate c = candidates[index];
                Score? current = score(c, can, options, pending);
                if (current == null) continue; // covers nothing new any more
                if (current.Value.CompareTo(queued) < 0 && queue.TryPeek(out _, out Score next) && current.Value.CompareTo(next) < 0)
                {
                    queue.Enqueue(index, current.Value); // stale score: re-queue with the real one
                    continue;
                }

                var actions = new List<WaterAction>();
                bool underfoot = c.Power > 0 && pending[c.StandIndex] && !c.Tiles.Contains(c.StandIndex);
                if (underfoot)
                {
                    actions.Add(WaterAction.Single(c.Stand, c.Stand));
                    pending[c.StandIndex] = false;
                }
                var targets = new List<Point>();
                foreach (int t in c.Tiles)
                {
                    if (!pending[t]) continue;
                    pending[t] = false;
                    targets.Add(new Point(t % map.width, t / map.width));
                }
                if (c.Power == 0)
                    actions.AddRange(targets.Select(t => WaterAction.Single(c.Stand, t)));
                else
                    actions.Add(new WaterAction { Action = WaterAction.Kind.Water, Stand = c.Stand, Origin = c.Origin, Facing = c.Facing, Power = c.Power, Targets = targets });
                stops.Add(actions);
            }
            return stops;
        }

        private static Score? score(Candidate c, CanProfile can, Options options, bool[] pending)
        {
            int crops = 0;
            foreach (int t in c.Tiles) if (pending[t]) crops++;
            if (crops == 0) return null;
            // A single-tile stop is one use per crop; a charged use is one use for all its crops.
            int uses = c.Power == 0 ? crops : 1;
            int cost = can.Cost(c.Power) * uses;
            int water = can.WaterUse(c.Power) * uses;
            int ticks = can.ActionTicks(c.Power, options.InstantCharge) * uses;
            if (c.Power > 0 && pending[c.StandIndex] && !c.Tiles.Contains(c.StandIndex))
            {
                crops++;
                cost += can.Cost(0);
                water += can.WaterUse(0);
                ticks += can.ActionTicks(0, options.InstantCharge);
            }
            double perStamina = cost == 0 ? crops * 1000.0 : crops / (double)cost;
            double lessWater = -water / (double)crops;
            double perTick = crops / (double)ticks;
            double closer = -c.WalkFromStart;
            // More crops from one standing tile means fewer stops to walk between.
            return options.OptimizeSpeed
                ? new Score(perTick, perStamina, lessWater, crops, closer)
                : new Score(perStamina, lessWater, perTick, crops, closer);
        }

        /// <summary>Merge uses that share a standing tile, then order the stops: nearest next by walking distance, then 2-opt.</summary>
        private static List<List<WaterAction>> orderStops(Map map, List<List<WaterAction>> chosen, Point start, int[] fromStart, Options options)
        {
            var byStand = new Dictionary<Point, List<WaterAction>>();
            var stops = new List<List<WaterAction>>();
            foreach (var actions in chosen)
            {
                Point stand = actions[0].Stand;
                if (byStand.TryGetValue(stand, out var existing)) existing.AddRange(actions);
                else
                {
                    byStand[stand] = actions;
                    stops.Add(actions);
                }
            }
            int n = stops.Count;
            if (n <= 1) return stops;

            // Walking distance between every pair of stops (and from the start).
            var dist = new int[n, n];
            var startDist = new int[n];
            for (int i = 0; i < n; i++)
            {
                int[] d = map.walkingDistances(stops[i][0].Stand);
                for (int j = 0; j < n; j++) dist[i, j] = Math.Max(0, d[map.index(stops[j][0].Stand)]);
                startDist[i] = Math.Max(0, fromStart[map.index(stops[i][0].Stand)]);
            }

            // Nearest next.
            var route = new List<int>(n);
            var used = new bool[n];
            int at = -1;
            for (int step = 0; step < n; step++)
            {
                int best = -1, bestD = int.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (used[j]) continue;
                    int d = at < 0 ? startDist[j] : dist[at, j];
                    if (d < bestD) { bestD = d; best = j; }
                }
                used[best] = true;
                route.Add(best);
                at = best;
            }

            // 2-opt on the open route (start fixed, free end), within a time budget.
            int D(int a, int b) => a < 0 ? startDist[b] : dist[a, b];
            var watch = Stopwatch.StartNew();
            bool improved = true;
            while (improved && watch.ElapsedMilliseconds < options.TwoOptMilliseconds)
            {
                improved = false;
                for (int i = 0; i < n - 1 && watch.ElapsedMilliseconds < options.TwoOptMilliseconds; i++)
                {
                    int before = i == 0 ? -1 : route[i - 1];
                    for (int j = i + 1; j < n; j++)
                    {
                        int after = j + 1 < n ? route[j + 1] : -2;
                        int oldCost = D(before, route[i]) + (after == -2 ? 0 : dist[route[j], after]);
                        int newCost = D(before, route[j]) + (after == -2 ? 0 : dist[route[i], after]);
                        if (newCost < oldCost)
                        {
                            route.Reverse(i, j - i + 1);
                            improved = true;
                        }
                    }
                }
            }

            return route.Select(i => stops[i]).ToList();
        }

        /// <summary>Total walking distance of a plan, for benchmarks and tests.</summary>
        public static int WalkingDistance(Map map, Point start, IEnumerable<WaterAction> actions)
        {
            int total = 0;
            Point at = start;
            foreach (WaterAction action in actions)
            {
                if (action.Stand == at) continue;
                total += Math.Max(0, map.walkingDistances(at)[map.index(action.Stand)]);
                at = action.Stand;
            }
            return total;
        }
    }
}
