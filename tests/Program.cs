using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using WaterBot.Framework;
using Config = WaterBot.WaterBot.Config;
using Mod = WaterBot.WaterBot;

var tests = new (string, Action)[]
{
    ("Diagonal crops are watered once", Diagonal),
    ("Planner stays within map bounds and covers accessible trellises", PlannerBounds),
    ("An unreachable crop is reported without getting stuck", Unreachable),
    ("Live soil is rechecked immediately before the tool effect", LiveSoil),
    ("Explicit tile survives stale facing/click and charged can state", ExactTarget),
    ("Cancelled tool callbacks cannot affect a restarted run", StaleRun),
    ("Missing paths and stalled movement return to idle", FailedMovement),
    ("Planning exceptions return to idle and allow retry", FailedPlanning),
    ("A crop that stays dry is retried once, then skipped and reported", FailedTool),
    ("Refill on finish exits without replaying the watering route", CompletionRefill),
    ("Empty-can refills resume with and without replanning", MidRunRefill),
    ("Paused ticks do not time out; location/tool changes cancel", Lifecycle),
    ("Bottomless and enchanted cans do not seek unnecessary refills", Bottomless),
    ("A timed-out animation releases player and tool ownership", AnimationTimeout),
    ("All 3x3 crop patterns (with trellises) are covered once", AllPatterns),
    ("Cancelled refill cannot restart or move a new run", CancelledRefill),
    ("No-water and low-stamina exits return to idle", ResourceLimits),
    ("Stamina is checked before every watering and never reaches 0", Stamina),
    ("Rows split by a path are watered from the path, 6 crops per stop", RowsFromPath),
    ("Stops are ordered by walking distance, not straight-line distance", WalkingOrder),
    ("Large farms plan quickly (no multi-second freeze)", LargeFarmSpeed),
    ("Stamina cost is rounded up and current stamina rounded down", Rounding),
    ("Charged patterns match the game's tilesAffected for every level and direction", Patterns),
    ("A 3x12 bed uses each can level's best patterns", BedByCanLevel),
    ("A gold can uses the 1x5 line on a single row", GoldUsesLine),
    ("A trellis row is watered from its end with a charged line", TrellisFromEnd),
    ("Charged watering reaches over a fence", OverFence),
    ("Crops where the farmer must stand are still watered (all 3x3 patterns, every can)", AllPatternsCharged),
    ("Low stamina lowers the charge level, then stops at 1", StaminaDrop),
    ("The can is refilled before a charged use that needs more water", WaterBudget),
    ("Efficient enchantment: watering costs no stamina", Efficient),
    ("Reaching enchantment uses the 5x5 pattern", Reaching),
    ("Realistic charging uses one game charge step per level; instant uses none", ChargeTiming),
    ("Stopping while charging releases the farmer", StopWhileCharging),
    ("If the game ends the tool use mid-charge, the run stops without watering", GameInterruptsCharge),
    ("A charged pattern never starts on a water tile", NoWaterOrigin),
    ("Charged watering can be turned off", ChargedOff),
    ("Speed mode waters everything with no more uses", SpeedMode),
};
foreach (var (name, test) in tests)
{
    test();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"{tests.Length} regression scenarios passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Reset(params Point[] crops)
{
    Context.IsWorldReady = true;
    Mod.config = new Config();
    Game1.currentLocation = new();
    Game1.player = new() { currentLocation = Game1.currentLocation, TilePoint = new(1, 1) };
    Game1.Messages.Clear();
    Game1.activeClickableMenu = null;
    Game1.TimePasses = true;
    foreach (Point point in crops) Game1.currentLocation.terrainFeatures[new(point.X, point.Y)] = new HoeDirt();
}
static WaterBotController Start()
{
    var bot = new WaterBotController(new Helper());
    bot.start(_ => { });
    return bot;
}
static void Step(WaterBotController bot)
{
    if (Game1.player.controller is { } movement)
    {
        Game1.player.TilePoint = movement.Destination;
        Game1.player.controller = null;
    }
    // The animation finishing calls the hook (Farmer.useTool). There is no animation while charging.
    if (Game1.player.UsingTool && Game1.player.toolOverrideFunction != null && !bot.charging)
    {
        Game1.player.toolOverrideFunction?.Invoke(Game1.player);
        Farmer.canMoveNow(Game1.player);
    }
    bot.tick();
}
static void Run(WaterBotController bot)
{
    for (int i = 0; i < 200000 && bot.active; i++) Step(bot);
    Check(!bot.active, "Run never finished");
}
static void UntilTool(WaterBotController bot)
{
    for (int i = 0; i < 100 && bot.active && !Game1.player.UsingTool; i++) Step(bot);
    Check(Game1.player.UsingTool, "Tool action never began");
}
static void Diagonal()
{
    Reset(new(2, 2), new(3, 3));
    Run(Start());
    Check(Game1.player.CurrentTool!.Uses.Count == 2 && Game1.player.CurrentTool.Uses.Distinct().Count() == 2, "Duplicate or missed watering");
}
static void PlannerBounds()
{
    // Crops on the map edge and corner, plus two trellises (blocked crops).
    Reset(new(7, 7), new(0, 0), new(3, 3), new(3, 4));
    Game1.currentLocation.Blocks.UnionWith(new[] { new Point(3, 3), new Point(3, 4) });
    var (map, actions) = PlanNow();
    Check(actions.All(a => map.isReachable(a.Stand)), "Blocked or unreachable stand");
    var targets = actions.SelectMany(a => a.Targets).ToList();
    Check(targets.Count == 4 && targets.Distinct().Count() == 4, "Incomplete planner coverage");
}
static void Unreachable()
{
    Reset(new Point(5, 5));
    for (int y = 4; y <= 6; y++) for (int x = 4; x <= 6; x++) Game1.currentLocation.Blocks.Add(new(x, y));
    var bot = Start(); Run(bot);
    Check(Game1.player.CurrentTool!.Uses.Count == 0, "Attempted inaccessible crop");
    Check(Game1.Messages.Last().Text == "process.remaining", "Skipped crop not reported");
}
static void LiveSoil()
{
    Reset(new Point(2, 2));
    var bot = Start(); UntilTool(bot);
    ((HoeDirt)Game1.currentLocation.terrainFeatures[new(2, 2)]).state.Value = 1;
    Run(bot);
    Check(Game1.player.CurrentTool!.Uses.Count == 0, "Wasted action on already-watered soil");
}
static void ExactTarget()
{
    Reset(new Point(2, 2));
    var bot = Start(); UntilTool(bot);
    Game1.player.lastClick = new(500, 500);
    Game1.player.FacingDirection = 0;
    Game1.player.toolPower.Value = 4;
    // The effect must remain idempotent if the animation invokes its callback twice.
    Game1.player.toolOverrideFunction!(Game1.player);
    Game1.player.toolOverrideFunction!(Game1.player);
    Run(bot);
    Check(Game1.player.CurrentTool!.Uses.SequenceEqual(new[] { new Point(2, 2) }), "Target changed");
    Check(Game1.player.toolPower.Value == 0, "Charged area watering was not reset");
}
static void StaleRun()
{
    Reset(new Point(2, 2));
    var bot = Start(); UntilTool(bot);
    var stale = Game1.player.toolOverrideFunction!;
    bot.stop(); bot.start(_ => { });
    UntilTool(bot);
    var fresh = Game1.player.toolOverrideFunction;
    stale(Game1.player);
    Check(bot.active && Game1.player.toolOverrideFunction == fresh && Game1.player.CurrentTool!.Uses.Count == 0, "Stale callback changed new run");
    Run(bot);
    Check(Game1.player.CurrentTool!.Uses.Count == 1, "New run failed");
}
static void FailedMovement()
{
    Reset(new Point(6, 6)); Game1.currentLocation.FailPaths = true;
    var bot = Start(); Run(bot);
    Check(Game1.Messages.Last().Text == "process.failed", "Missing path not reported");
    Reset(new Point(6, 6)); bot = Start(); bot.tick();
    Check(Game1.player.controller != null, "Expected movement");
    for (int i = 0; i < 181; i++) bot.tick();
    Check(!bot.active && Game1.player.controller == null, "Stalled movement left active");
}
static void FailedPlanning()
{
    Reset(new Point(2, 2)); Game1.currentLocation.ThrowOnScan = true;
    var bot = Start(); Check(!bot.active, "Planning exception left active");
    Game1.currentLocation.ThrowOnScan = false; bot.start(_ => { }); Run(bot);
    Check(Game1.player.CurrentTool!.Uses.Count == 1, "Could not restart after planning failure");
}
static void FailedTool()
{
    // (2,2) never gets wet; (5,5) is fine. The bot tries (2,2) twice, skips it, waters (5,5), and reports 1 dry crop.
    Reset(new(2, 2), new(5, 5)); Game1.currentLocation.IgnoreTiles.Add(new(2, 2));
    var bot = Start(); Run(bot);
    var uses = Game1.player.CurrentTool!.Uses;
    Check(uses.Count(u => u == new Point(2, 2)) == 2, "Retry was not bounded to two attempts");
    Check(Map.tileNeedsWatering(2, 2) && !Map.tileNeedsWatering(5, 5), "Run stopped instead of skipping the failing crop");
    Check(Game1.Messages.Last().Text == "process.remaining", "Skipped crop not reported");
}
static void CompletionRefill()
{
    foreach (bool redo in new[] { false, true })
    {
        Reset(new Point(2, 2)); Mod.config.RefillOnFinish = true; Mod.config.RefillIfLower = 100; Mod.config.RedoPathOnRefill = redo;
        Game1.currentLocation.Water.Add(new(0, 1)); Game1.currentLocation.Blocks.Add(new(0, 1));
        var bot = Start(); Run(bot);
        Check(Game1.player.CurrentTool!.Uses.Count == 2 && Game1.player.CurrentTool.WaterLeft == 40, "Completion refill replayed route or failed");
        Check(Game1.Messages.Last().Text == "process.end", "Completion refill did not finish");
    }
}
static void MidRunRefill()
{
    foreach (bool redo in new[] { false, true })
    {
        Reset(new(2, 2), new(3, 3)); Mod.config.RedoPathOnRefill = redo;
        Game1.currentLocation.Water.Add(new(0, 1)); Game1.currentLocation.Blocks.Add(new(0, 1));
        Game1.player.CurrentTool!.WaterLeft = 1;
        var bot = Start(); Run(bot);
        Check(Game1.player.CurrentTool.Uses.Count == 3, "Mid-run refill missed/duplicated work");
        Check(!Map.tileNeedsWatering(2, 2) && !Map.tileNeedsWatering(3, 3), "Crop missed after refill");
    }
}
static void Lifecycle()
{
    Reset(new Point(6, 6)); var bot = Start(); bot.tick(); Game1.TimePasses = false;
    for (int i = 0; i < 400; i++) bot.tick();
    Check(bot.active, "Paused movement timed out");
    Game1.currentLocation = new(); bot.tick(); Check(!bot.active, "Warp left active");
    Reset(new Point(2, 2)); bot = Start(); UntilTool(bot); Game1.player.CurrentTool = new(); bot.tick();
    Check(!bot.active && Game1.player.toolOverrideFunction == null && !Game1.player.UsingTool, "Tool change leaked bot state");
}
static void Bottomless()
{
    Reset(new(2, 2), new(3, 3));
    Game1.player.CurrentTool!.IsBottomless = true;
    Game1.player.CurrentTool.WaterLeft = 1;
    Run(Start());
    Check(!Map.tileNeedsWatering(2, 2) && !Map.tileNeedsWatering(3, 3) && Game1.player.CurrentTool.WaterLeft == 1,
        "Bottomless can incorrectly needed refill");
    Reset(new Point(2, 2));
    Game1.player.CurrentTool!.WaterLeft = 0;
    Game1.player.hasWateringCanEnchantment = true;
    Run(Start());
    Check(!Map.tileNeedsWatering(2, 2), "Enchanted can incorrectly needed refill");
}

static void AnimationTimeout()
{
    Reset(new Point(2, 2)); var bot = Start(); UntilTool(bot);
    for (int i = 0; i < 300; i++) bot.tick();
    Check(!bot.active && !Game1.player.UsingTool && Game1.player.toolOverrideFunction == null, "Animation timeout leaked ownership");
}
static void AllPatterns()
{
    for (int mask = 0; mask < 512; mask++)
    {
        var crops = Enumerable.Range(0, 9).Where(i => (mask & (1 << i)) != 0).Select(i => new Point(3 + i % 3, 3 + i / 3)).ToArray();
        Reset(crops);
        // Mix walkable crops and trellises while preserving access around the plot.
        Game1.currentLocation.Blocks.UnionWith(crops.Where(p => (p.X + p.Y) % 2 == 0));
        Run(Start());
        var uses = Game1.player.CurrentTool!.Uses;
        Check(uses.Count == crops.Length && uses.Distinct().Count() == crops.Length,
            $"Incomplete/duplicate watering for pattern {mask}");
    }
}
static void CancelledRefill()
{
    foreach (bool redo in new[] { false, true })
    {
        Reset(new Point(2, 2)); Mod.config.RedoPathOnRefill = redo;
        Game1.player.CurrentTool!.WaterLeft = 0;
        Game1.currentLocation.Water.Add(new(0, 1));
        var bot = Start(); UntilTool(bot);
        var stale = Game1.player.toolOverrideFunction!;
        bot.stop();
        stale(Game1.player);
        for (int i = 0; i < 60; i++) bot.tick();
        Check(!bot.active && Game1.player.controller == null && Game1.player.CurrentTool.Uses.Count == 0,
            "Cancelled refill resumed work");
        bot.start(_ => { }); Run(bot);
        Check(Game1.player.CurrentTool.Uses.Count == 2, "Could not restart after cancelled refill");
    }
}
static void ResourceLimits()
{
    Reset(new Point(2, 2)); Game1.player.CurrentTool!.WaterLeft = 0;
    var bot = Start(); Run(bot);
    Check(Game1.Messages.Last().Text == "process.waterless", "Missing water not reported");
    Reset(new Point(2, 2)); Game1.player.Stamina = 2;
    bot = Start(); Run(bot);
    Check(Game1.Messages.Last().Text == "process.exhausted" && Game1.player.CurrentTool!.Uses.Count == 0,
        "Low stamina consumed an action");
}

static void Stamina()
{
    // At 2 or below, the bot must not start (each action costs up to 2).
    foreach (float stamina in new[] { 2f, 1f, 0.5f })
    {
        Reset(new Point(2, 2)); Game1.player.Stamina = stamina;
        var bot = Start();
        Check(!bot.active && Game1.player.CurrentTool!.Uses.Count == 0 && Game1.player.Stamina == stamina
            && Game1.Messages.Last().Text == "process.exhausted", $"Acted or started at stamina {stamina}");
    }

    // 7 -> 5 -> 3 -> 1, then stop with crops still dry.
    Reset(Enumerable.Range(0, 9).Select(i => new Point(3 + i % 3, 3 + i / 3)).ToArray());
    Game1.player.Stamina = 7;
    Run(Start());
    Check(Game1.player.CurrentTool!.Uses.Count == 3 && Game1.player.Stamina == 1, "Wrong number of actions for stamina 7");
    Check(Game1.Messages.Last().Text == "process.exhausted", "Exhaustion not reported");

    // A retry is an action too: 4 -> 2 after a failed attempt, so no retry.
    Reset(new Point(2, 2)); Game1.currentLocation.IgnoreWatering = true; Game1.player.Stamina = 4;
    Run(Start());
    Check(Game1.player.CurrentTool!.Uses.Count == 1 && Game1.player.Stamina == 2
        && Game1.Messages.Last().Text == "process.exhausted", "Retry ignored low stamina");

    // Refilling costs no stamina, so it still happens at 2; only the next watering is refused.
    Reset(new(2, 2), new(3, 3)); Game1.player.Stamina = 4; Game1.player.CurrentTool!.WaterLeft = 1;
    Game1.currentLocation.Water.Add(new(0, 1)); Game1.currentLocation.Blocks.Add(new(0, 1));
    Run(Start());
    Check(Game1.player.CurrentTool.Uses.Count == 2 && Game1.player.CurrentTool.Uses[1] == new Point(0, 1)
        && Game1.player.CurrentTool.WaterLeft == 40 && Game1.player.Stamina == 2
        && Game1.Messages.Last().Text == "process.exhausted", "Low stamina blocked a refill");

    // Stamina can drop during the animation; the tool effect checks again before spending it.
    Reset(new Point(2, 2));
    var running = Start(); UntilTool(running);
    Game1.player.Stamina = 2;
    Run(running);
    Check(Game1.player.CurrentTool!.Uses.Count == 0 && Game1.player.Stamina == 2
        && Game1.Messages.Last().Text == "process.exhausted", "Tool effect ignored low stamina");
}

static void Farm(int width, int height, Point player, IEnumerable<Point> crops)
{
    Reset(crops.ToArray());
    Game1.currentLocation.map.Layers[0].LayerWidth = width;
    Game1.currentLocation.map.Layers[0].LayerHeight = height;
    Game1.player.TilePoint = player;
}
static (Map, List<WaterAction>) PlanNow(bool speed = false)
{
    Map map = new(); map.loadMap();
    var can = CanProfile.Read(Game1.player, Game1.player.CurrentTool!, Mod.config.UseChargedWatering);
    var options = new WaterBot.Framework.Planner.Options { OptimizeSpeed = speed, InstantCharge = Mod.config.InstantCharge };
    return (map, WaterBot.Framework.Planner.Plan(map, can, Game1.player.Stamina, options, _ => { }, Game1.player.TilePoint));
}
static void Can(int level, bool reaching = false, bool efficient = false)
{
    var can = Game1.player.CurrentTool!;
    can.UpgradeLevel = level;
    can.waterCanMax = new[] { 40, 55, 70, 85, 100 }[level];
    can.WaterLeft = can.waterCanMax;
    can.Reaching = reaching;
    can.IsEfficient = efficient;
    Game1.player.Stamina = 270;
}
static bool AllWet(IEnumerable<Point> crops) => crops.All(c => !Map.tileNeedsWatering(c.X, c.Y));
static void RowsFromPath()
{
    // Two 60-crop rows (y=5 and y=7) with a walking path between them (y=6).
    Farm(80, 65, new(2, 2), Enumerable.Range(5, 60).SelectMany(x => new[] { new Point(x, 5), new Point(x, 7) }));
    var (_, actions) = PlanNow();
    var stands = actions.Select(a => a.Stand).Distinct().ToList();
    Check(stands.Count == 20, $"Expected 20 stops of 6 crops, got {stands.Count}");
    Check(stands.All(s => s.Y == 6), "A stop was not on the path between the rows");
}
static void WalkingOrder()
{
    // A wall at x=3 (open only at the bottom) separates the player from crop A.
    // A is 3 tiles away in a straight line but ~35 tiles to walk; B is 6 away on the player's side.
    Point a = new(5, 10), b = new(2, 16);
    Farm(30, 30, new(2, 10), new[] { a, b });
    for (int y = 0; y <= 25; y++) Game1.currentLocation.Blocks.Add(new(3, y));
    var (_, actions) = PlanNow();
    Check(actions.Count == 2 && actions[0].Targets[0] == b && actions[1].Targets[0] == a, "Planner ignored the wall when ordering stops");
}
static void LargeFarmSpeed()
{
    // 480 crops with no side contact (the old planner's worst case: ~30 s) and 480 in rows, starter and iridium+reaching cans.
    var scattered = Enumerable.Range(0, 35).SelectMany(i => Enumerable.Range(0, 28).Select(j => new Point(5 + i * 2, 5 + j * 2))).Take(480).ToList();
    var rows = Enumerable.Range(0, 8).SelectMany(r => Enumerable.Range(5, 60).Select(x => new Point(x, 5 + r * 2))).ToList();
    foreach (var (name, crops) in new[] { ("scattered", scattered), ("rows", rows) })
    foreach (int level in new[] { 0, 4 })
    {
        Farm(80, 65, new(2, 2), crops); Can(level, reaching: level == 4);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (map, actions) = PlanNow();
        watch.Stop();
        int walk = WaterBot.Framework.Planner.WalkingDistance(map, new(2, 2), actions);
        Console.WriteLine($"     {name}, can level {level}{(level == 4 ? "+Reaching" : "")}: {crops.Count} crops -> {actions.Count} uses, " +
            $"stamina {actions.Sum(a => CanProfile.CostUp(a.Power, 0, false))}, walk {walk}, planned in {watch.ElapsedMilliseconds} ms");
        Check(actions.SelectMany(a => a.Targets).Distinct().Count() == crops.Count, $"Lost crops ({name}, level {level})");
        Check(watch.ElapsedMilliseconds < 2000, $"Planning {name} took {watch.ElapsedMilliseconds} ms");
    }
}
static void Rounding()
{
    // Cost rounds up: 2 - 0.5 = 1.5 -> 2; 2 - 1.0 = 1 (no float error); 10 - 1.0 = 9.
    Check(CanProfile.CostUp(0, 0, false) == 2 && CanProfile.CostUp(0, 5, false) == 2 && CanProfile.CostUp(0, 10, false) == 1, "Single-tile cost");
    Check(CanProfile.CostUp(4, 0, false) == 10 && CanProfile.CostUp(4, 5, false) == 10 && CanProfile.CostUp(4, 10, false) == 9, "Iridium cost");
    Check(CanProfile.CostUp(5, 0, false) == 12 && CanProfile.CostUp(3, 0, true) == 0, "Reaching / Efficient cost");
    // Stamina rounds down, and at least 1 whole point must be left.
    Check(!CanProfile.CanAffordCost(2.9f, 2) && CanProfile.CanAffordCost(3.0f, 2) && CanProfile.CanAffordCost(2.0f, 1), "Rounding rule");
    Check(!CanProfile.CanAffordCost(10.99f, 10) && CanProfile.CanAffordCost(11f, 10), "Rounding rule (iridium)");
    // In a run: 2.9 stamina at farming level 0 must not water.
    Reset(new Point(2, 2)); Game1.player.Stamina = 2.9f;
    var bot = Start();
    Check(!bot.active && Game1.player.CurrentTool!.Uses.Count == 0 && Game1.Messages.Last().Text == "process.exhausted", "Watered at 2.9 stamina");
    // At farming level 10 a single tile costs 1, so 2.0 waters once and ends at 1.
    Reset(new(2, 2), new(4, 4)); Game1.player.Stamina = 2f; Game1.player.FarmingLevel = 10;
    Run(Start());
    Check(Game1.player.CurrentTool!.Uses.Count == 1 && Math.Abs(Game1.player.Stamina - 1f) < 0.001f, "Farming level 10 rounding");
}
static void Patterns()
{
    for (int p = 0; p <= CanProfile.MaxSupportedPower; p++)
    for (int d = 0; d < 4; d++)
    {
        Point origin = new(20, 20);
        var mine = CanProfile.Pattern(p, d).Select(o => new Point(origin.X + o.X, origin.Y + o.Y)).ToHashSet();
        var game = GameShapes.TilesAffected(origin, p, d).ToHashSet();
        Check(mine.SetEquals(game), $"Pattern differs at power {p}, facing {d}");
        Check(CanProfile.VerifyPatterns((o, power) => GameShapes.TilesAffected(o, power, d), d) == null, "Self-check failed");
    }
}
static List<Point> Bed() => Enumerable.Range(5, 12).SelectMany(x => Enumerable.Range(5, 3).Select(y => new Point(x, y))).ToList();
static void BedByCanLevel()
{
    // One bed 3 wide x 12 long (36 crops) with paths around it, farming level 0.
    // Expected uses and stamina: starter 36/72, copper 12/48, steel 9/48, gold 4/32, iridium 2/20.
    var expected = new[] { (36, 72), (12, 48), (9, 48), (4, 32), (2, 20) };
    for (int level = 0; level <= 4; level++)
    {
        var crops = Bed();
        Farm(30, 20, new(2, 6), crops); Can(level);
        Run(Start());
        var can = Game1.player.CurrentTool!;
        int uses = can.Uses.Count, stamina = (int)Math.Round(270 - Game1.player.Stamina);
        Console.WriteLine($"     can level {level}: {uses} uses, {stamina} stamina, powers [{string.Join(",", can.UsePowers.Distinct().OrderBy(x => x))}]");
        Check(AllWet(crops), $"Level {level}: crops left dry");
        Check(uses <= expected[level].Item1 && stamina <= expected[level].Item2,
            $"Level {level}: {uses} uses / {stamina} stamina, expected at most {expected[level].Item1} / {expected[level].Item2}");
    }
}
static void GoldUsesLine()
{
    var crops = Enumerable.Range(5, 10).Select(x => new Point(x, 5)).ToList();
    Farm(30, 20, new(2, 5), crops); Can(3);
    Run(Start());
    var can = Game1.player.CurrentTool!;
    Check(AllWet(crops) && can.Uses.Count == 2 && can.UsePowers.All(p => p == 2), $"Expected two 1x5 lines, got powers [{string.Join(",", can.UsePowers)}]");
}
static void TrellisFromEnd()
{
    // Five trellis crops (blocked) in a row, with fences above and below: only the ends are open.
    var crops = Enumerable.Range(5, 5).Select(x => new Point(x, 5)).ToList();
    foreach (bool steel in new[] { false, true })
    {
        Farm(30, 20, new(2, 5), crops); Can(steel ? 2 : 0);
        Game1.currentLocation.Blocks.UnionWith(crops);
        for (int x = 5; x <= 9; x++) { Game1.currentLocation.Blocks.Add(new(x, 4)); Game1.currentLocation.Blocks.Add(new(x, 6)); }
        Run(Start());
        var can = Game1.player.CurrentTool!;
        if (steel) Check(AllWet(crops) && can.Uses.Count == 1 && can.UsePowers[0] == 2, "Steel can didn't water the trellis row from its end");
        else Check(can.Uses.Count == 2 && Game1.Messages.Last().Text == "process.remaining", "Starter can should reach only the two end trellises");
    }
}
static void OverFence()
{
    // A crop fenced in on all sides. The starter can can't reach it; a copper line can, from outside.
    Point crop = new(8, 5);
    foreach (int level in new[] { 0, 1 })
    {
        Farm(30, 20, new(2, 5), new[] { crop }); Can(level);
        for (int y = 4; y <= 6; y++) for (int x = 7; x <= 9; x++) if (new Point(x, y) != crop) Game1.currentLocation.Blocks.Add(new(x, y));
        Run(Start());
        if (level == 0) Check(!AllWet(new[] { crop }) && Game1.Messages.Last().Text == "process.remaining", "Starter can watered through a fence");
        else Check(AllWet(new[] { crop }) && Game1.player.CurrentTool!.Uses.Count == 1, "Copper line didn't reach over the fence");
    }
}
static void AllPatternsCharged()
{
    // Every 3x3 crop pattern inside a fenced 3x3 plot with one gap, so some uses must stand on crops.
    foreach (int level in new[] { 1, 2, 3, 4 })
    for (int mask = 1; mask < 512; mask += level == 4 ? 1 : 7)
    {
        var crops = Enumerable.Range(0, 9).Where(i => (mask & (1 << i)) != 0).Select(i => new Point(5 + i % 3, 5 + i / 3)).ToList();
        Farm(20, 20, new(2, 6), crops); Can(level, reaching: level == 4 && mask % 2 == 0);
        for (int y = 4; y <= 8; y++) for (int x = 4; x <= 8; x++)
            if ((x == 4 || x == 8 || y == 4 || y == 8) && !(x == 4 && y == 6)) Game1.currentLocation.Blocks.Add(new(x, y));
        Run(Start());
        Check(AllWet(crops), $"Level {level}, pattern {mask}: crops left dry");
        Check(Game1.player.LowestStamina >= 1, $"Level {level}, pattern {mask}: stamina went below 1");
    }
}
static void StaminaDrop()
{
    // Iridium at 15 stamina: one 3x6 (cost 10, leaves 5), then only a copper-size line (cost 4, leaves 1), then stop.
    var crops = Bed();
    Farm(30, 20, new(2, 6), crops); Can(4); Game1.player.Stamina = 15;
    Run(Start());
    var can = Game1.player.CurrentTool!;
    Check(can.UsePowers.SequenceEqual(new[] { 4, 1 }), $"Expected powers [4,1], got [{string.Join(",", can.UsePowers)}]");
    Check(Math.Abs(Game1.player.Stamina - 1f) < 0.001f && Game1.player.LowestStamina >= 1, $"Ended at {Game1.player.Stamina} stamina");
    Check(Game1.Messages.Last().Text == "process.exhausted", "Exhaustion not reported");
}
static void WaterBudget()
{
    // Copper can with 1 water left: a 1x3 line needs 2, so the bot refills first.
    var crops = Enumerable.Range(5, 3).Select(x => new Point(x, 5)).ToList();
    Farm(30, 20, new(2, 5), crops); Can(1); Game1.player.CurrentTool!.WaterLeft = 1;
    Game1.currentLocation.Water.Add(new(0, 5)); Game1.currentLocation.Blocks.Add(new(0, 5));
    Run(Start());
    var can = Game1.player.CurrentTool!;
    Check(can.Uses.Count == 2 && can.Uses[0] == new Point(0, 5) && can.UsePowers[1] == 1, "Did not refill before the charged use");
    Check(AllWet(crops) && can.LowestWater >= 0, "Water went negative or crops missed");
}
static void Efficient()
{
    var crops = Bed();
    Farm(30, 20, new(2, 6), crops); Can(4, efficient: true); Game1.player.Stamina = 3;
    Run(Start());
    Check(AllWet(crops) && Game1.player.Stamina == 3, $"Efficient can used stamina ({Game1.player.Stamina})");
}
static void Reaching()
{
    var crops = Enumerable.Range(0, 25).Select(i => new Point(5 + i % 5, 5 + i / 5)).ToList();
    Farm(30, 20, new(2, 7), crops); Can(4, reaching: true);
    Run(Start());
    var can = Game1.player.CurrentTool!;
    Check(AllWet(crops) && can.Uses.Count == 1 && can.UsePowers[0] == 5, $"Expected one 5x5, got powers [{string.Join(",", can.UsePowers)}]");
}
static void ChargeTiming()
{
    var crops = Enumerable.Range(5, 3).Select(x => new Point(x, 5)).ToList();
    foreach (bool instant in new[] { false, true })
    {
        Farm(30, 20, new(4, 5), crops); Can(1); Mod.config.InstantCharge = instant;
        var bot = Start();
        int ticks = 0;
        while (bot.active && ticks < 1000) { Step(bot); ticks++; }
        var can = Game1.player.CurrentTool!;
        Check(AllWet(crops) && can.UsePowers.SequenceEqual(new[] { 1 }), "Charged use missing");
        if (instant) Check(Game1.player.ChargeSteps == 0, "Instant charge still stepped");
        else Check(Game1.player.ChargeSteps == 1 && ticks >= 36, $"Realistic charge: {Game1.player.ChargeSteps} steps in {ticks} ticks");
    }
}
static void StopWhileCharging()
{
    Farm(30, 20, new(4, 5), Enumerable.Range(5, 3).Select(x => new Point(x, 5))); Can(1);
    var bot = Start();
    for (int i = 0; i < 100 && !bot.charging; i++) Step(bot);
    Check(bot.charging && Game1.player.UsingTool, "Never started charging");
    bot.stop();
    Check(!bot.active && !Game1.player.UsingTool && Game1.player.CanMove && Game1.player.toolPower.Value == 0, "Farmer still held after stopping");
    for (int i = 0; i < 100; i++) Step(bot);
    Check(Game1.player.CurrentTool!.Uses.Count == 0, "Watered after being stopped");
}
static void GameInterruptsCharge()
{
    Farm(30, 20, new(4, 5), Enumerable.Range(5, 3).Select(x => new Point(x, 5))); Can(1);
    var bot = Start();
    for (int i = 0; i < 100 && !bot.charging; i++) bot.tick();
    Check(bot.charging && Game1.player.toolOverrideFunction != null, "Charging guard not installed");
    // What Farmer.useTool does if the game ends the tool use early: call the override.
    Game1.player.toolOverrideFunction!(Game1.player);
    Check(!bot.active && !Game1.player.UsingTool && Game1.player.toolOverrideFunction == null && Game1.player.toolPower.Value == 0,
        "Run or farmer state left behind");
    Check(Game1.player.CurrentTool!.Uses.Count == 0 && Game1.Messages.Last().Text == "process.failed", "Watered or not reported");
}
static void NoWaterOrigin()
{
    // Pond tile at (5,5) right in front of the obvious standing tile (4,5).
    var crops = new List<Point> { new(6, 5), new(7, 5) };
    Farm(30, 20, new(2, 5), crops); Can(1);
    Game1.currentLocation.Water.Add(new(5, 5)); Game1.currentLocation.Blocks.Add(new(5, 5));
    Run(Start());
    var can = Game1.player.CurrentTool!;
    Check(AllWet(crops) && !can.Uses.Contains(new Point(5, 5)), "A charged use started on water");
}
static void ChargedOff()
{
    var crops = Bed();
    Farm(30, 20, new(2, 6), crops); Can(4); Mod.config.UseChargedWatering = false;
    Run(Start());
    Check(AllWet(crops) && Game1.player.CurrentTool!.UsePowers.All(p => p == 0), "Charged watering used while turned off");
}
static void SpeedMode()
{
    foreach (int level in new[] { 2, 3, 4 })
    {
        int stamUses, speedUses;
        Farm(30, 20, new(2, 6), Bed()); Can(level);
        Run(Start()); stamUses = Game1.player.CurrentTool!.Uses.Count;
        Farm(30, 20, new(2, 6), Bed()); Can(level); Mod.config.Optimize = "Speed";
        Run(Start()); speedUses = Game1.player.CurrentTool!.Uses.Count;
        Check(AllWet(Bed()) && speedUses <= stamUses, $"Level {level}: speed mode used {speedUses} uses vs {stamUses}");
    }
}

sealed class Helper : IModHelper { public Translations Translation { get; } = new(); }
