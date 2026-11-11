using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using WaterBot.Framework;
using Config = WaterBot.WaterBot.Config;
using Mod = WaterBot.WaterBot;

var tests = new (string, Action)[]
{
    ("Diagonal crops are watered once", Diagonal),
    ("Planner stays within map bounds and covers accessible trellises", Planner),
    ("An unreachable crop is reported without getting stuck", Unreachable),
    ("Live soil is rechecked immediately before the tool effect", LiveSoil),
    ("Explicit tile survives stale facing/click and charged can state", ExactTarget),
    ("Cancelled tool callbacks cannot affect a restarted run", StaleRun),
    ("Missing paths and stalled movement return to idle", FailedMovement),
    ("Planning exceptions return to idle and allow retry", FailedPlanning),
    ("Unsuccessful watering is retried once and never marked complete", FailedTool),
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
    if (Game1.player.UsingTool)
    {
        Game1.player.toolOverrideFunction?.Invoke(Game1.player);
        Farmer.canMoveNow(Game1.player);
    }
    bot.tick();
}
static void Run(WaterBotController bot)
{
    for (int i = 0; i < 300 && bot.active; i++) Step(bot);
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
static void Planner()
{
    // Crops on the map edge and corner, plus two trellises (blocked crops).
    Reset(new(7, 7), new(0, 0), new(3, 3), new(3, 4));
    Game1.currentLocation.Blocks.UnionWith(new[] { new Point(3, 3), new Point(3, 4) });
    Map map = new(); map.loadMap();
    List<Point> targets = new();
    foreach (var stop in map.planStops(_ => { }))
    {
        Check(map.isReachable(stop.getStand()), "Blocked or unreachable stand");
        while (!stop.isDone()) targets.Add(stop.Pop());
    }
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
    Reset(new Point(2, 2)); Game1.currentLocation.IgnoreWatering = true;
    var bot = Start(); Run(bot);
    Check(Game1.player.CurrentTool!.Uses.Count == 2, "Retry was not bounded to two attempts");
    Check(Map.tileNeedsWatering(2, 2), "Missed crop was marked watered");
    Check(Game1.Messages.Last().Text == "process.failed", "Failed watering not reported");
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
static void RowsFromPath()
{
    // Two 60-crop rows (y=5 and y=7) with a walking path between them (y=6).
    Farm(80, 65, new(2, 2), Enumerable.Range(5, 60).SelectMany(x => new[] { new Point(x, 5), new Point(x, 7) }));
    Map map = new(); map.loadMap();
    var stops = map.planStops(_ => { });
    Check(stops.Count == 20, $"Expected 20 stops of 6 crops, got {stops.Count}");
    Check(stops.All(s => s.getStand().Y == 6), "A stop was not on the path between the rows");
}
static void WalkingOrder()
{
    // A wall at x=3 (open only at the bottom) separates the player from crop A.
    // A is 3 tiles away in a straight line but ~35 tiles to walk; B is 6 away on the player's side.
    Point a = new(5, 10), b = new(2, 16);
    Farm(30, 30, new(2, 10), new[] { a, b });
    for (int y = 0; y <= 25; y++) Game1.currentLocation.Blocks.Add(new(3, y));
    Map map = new(); map.loadMap();
    var stops = map.planStops(_ => { });
    Check(stops.Count == 2 && stops[0].Pop() == b && stops[1].Pop() == a, "Planner ignored the wall when ordering stops");
}
static void LargeFarmSpeed()
{
    // 480 crops with no side contact (the old planner's worst case: ~30 s) and 480 in rows.
    var scattered = Enumerable.Range(0, 35).SelectMany(i => Enumerable.Range(0, 28).Select(j => new Point(5 + i * 2, 5 + j * 2))).Take(480);
    var rows = Enumerable.Range(0, 8).SelectMany(r => Enumerable.Range(5, 60).Select(x => new Point(x, 5 + r * 2)));
    foreach (var (name, crops) in new[] { ("scattered", scattered), ("rows", rows) })
    {
        Farm(80, 65, new(2, 2), crops);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Map map = new(); map.loadMap();
        var stops = map.planStops(_ => { });
        watch.Stop();
        Console.WriteLine($"     {name}: {crops.Count()} crops -> {stops.Count} stops, planned in {watch.ElapsedMilliseconds} ms");
        Check(stops.Sum(s => { int n = 0; while (!s.isDone()) { s.Pop(); n++; } return n; }) == crops.Count(), $"Lost crops ({name})");
        Check(watch.ElapsedMilliseconds < 2000, $"Planning {name} took {watch.ElapsedMilliseconds} ms");
    }
}

sealed class Helper : IModHelper { public Translations Translation { get; } = new(); }
