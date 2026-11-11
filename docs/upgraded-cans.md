# Upgraded watering cans: design, build and test guide

Branch: `feature/upgraded-cans` (from `perf/whole-farm-planner`). Status as of 2026-11-11:

- 38 regression scenarios pass on .NET 8.0.428.
- The mod compiles against the real `Stardew Valley.dll` 1.6.15 (with stand-ins for the SMAPI interfaces, copied from SMAPI's source). It has **not** been built against real SMAPI or tested in the game.
- Game behaviour the design relies on was checked in the decompiled game code: see [game-mechanics.md](game-mechanics.md).

## What the bot does now

**Can abilities** (`Framework/CanProfile.cs`), read live before planning and before every watering:

| Can | Max charge level | Patterns it can use |
|---|---|---|
| Starter | 0 | single tile |
| Copper | 1 | + 1×3 line |
| Steel | 2 | + 1×5 line |
| Gold | 3 | + 3×3 |
| Iridium | 4 | + 3 wide × 6 deep |
| Iridium + Reaching | 5 | + 5×5 |

A pattern starts at the tile in front of the farmer and points the way they face. It reaches over fences, trellises and buildings.

**Stamina** (your rule, with rounding):
- Cost of a watering at charge level p: `ceil(2 × (p + 1) − 0.1 × FarmingLevel)`, worked out in tenths so float error can't round 1.0 up to 2. 0 with the Efficient enchantment.
- The bot only waters if `floor(Stamina) − cost ≥ 1`, so stamina never ends below 1 (as shown on the energy bar).
- Checked when starting, before walking to each watering, before a retry, and once more when the effect fires.
- When the planned level is no longer affordable, the rest is planned again with the levels still affordable. When even a single tile isn't, the bot stops with "Tired of Watering".
- Walking and refilling cost no stamina and are never blocked by it.

**Water:** a use at level p takes `p + 1` water (none for a bottomless can). The bot refills before any use that needs more than is left, so the can never goes negative (the game itself would allow that).

**Planning** (`Framework/Planner.cs`), two steps:
1. **Choose the uses (greedy set cover).**
   - List every way to water each dry crop: single-tile stops (stand on or next to crops, water everything in reach one tile at a time) and every affordable charged pattern, at every level and in every direction, from a reachable standing tile.
   - Patterns that would start on a water tile are left out: the game refills the can instead of watering in that case.
   - Then repeatedly take the use that waters the most still-dry crops per stamina point. Ties go to less water per crop, then less time per crop, then more crops from one spot, then a shorter walk from the start.
   - With `Optimize = Speed`, time per crop comes first instead.
   - A charged pattern never waters the tile underfoot. If the farmer would stand on a dry crop, a single-tile watering of it is added at the same stop and counted in the cost.
2. **Order the stops by walking distance:** nearest next, then 2-opt (50 ms budget).

**Doing a charged watering** (`Framework/WaterBotController.cs`, new `Charging` state):
1. Walk to the standing tile.
2. Face the planned direction.
3. Charge one level per `600 ms × AnimationSpeedModifier` using the game's own `Farmer.toolPowerIncrease` (sound, coloured flash, shake). With `InstantCharge`, set the level at once.
4. Play the game's watering animation (`Tool.endUsing`). Its `Farmer.useTool` callback runs the bot's hook, which sets the facing and charge level and calls `WateringCan.DoFunction` on the pattern's first tile.
5. Check every target crop. Dry ones get one more try (through re-planning); a crop dry after two tries is skipped and counted in the end report, and the run carries on.

While charging, the bot also installs a guard hook. If the game ends the tool use itself (an event starts, for example), the run stops instead of the game watering wherever the mouse points.

**Self-check.** The first time the bot starts in a session, the mod compares its patterns with the game's own `Tool.tilesAffected` (through SMAPI reflection). If they differ, for example after a game update, charged watering is turned off and a warning is logged.

## Settings

| Setting | Default | Effect |
|---|---|---|
| `UseChargedWatering` | `true` | Off: single tiles only, as before |
| `InstantCharge` | `false` | On: skip the hold time |
| `Optimize` | `Stamina` | `Speed`: least time per crop first |

All three are in the Generic Mod Config Menu, with English text in `i18n/default.json`.

## Results (test stand-ins, farming level 0)

One bed 3 wide × 12 long (36 crops):

| Can | Uses | Stamina | Patterns used |
|---|---|---|---|
| Starter | 36 | 72 | single tiles |
| Copper | 12 | 48 | 1×3 |
| Steel | 8 | 44 | 1×5 + 1×3 |
| Gold | 4 | 32 | 3×3 |
| Iridium | 2 | 20 | 3×6 |

Farm-sized map (80 × 65), 480 crops:

| Layout | Can | Uses | Stamina | Walking (tiles) | Planning time |
|---|---|---|---|---|---|
| Rows with 1-tile paths | Starter | 480 | 960 | 248 | 67 ms |
| Rows with 1-tile paths | Iridium + Reaching | 34 | 388 | 188 | 94 ms |
| Scattered (no crops side by side) | Starter | 480 | 960 | 492 | 109 ms |
| Scattered (no crops side by side) | Iridium + Reaching | 61 | 668 | 357 | 157 ms |

Real-game planning will be a little slower (collision checks in `loadMap`).

## Building and testing on another computer

1. **Regression tests:** `dotnet run --project tests/WaterBot.RegressionTests.csproj`. Expected: `38 regression scenarios passed.` (only the old `console` type-name warning). The bed and large-farm tests print the numbers above.
2. **Build with SMAPI installed:** `dotnet build WaterBot/WaterBot.csproj -p:EnableModDeploy=false -p:EnableModZip=false`, then `dotnet build WaterBot/WaterBot.csproj` to deploy into `Mods/WaterBot`.
3. **In game.** Watch the SMAPI console: a pattern self-check warning or any "WaterBot stopped safely" error is important.

### In-game checklist for upgraded cans

1. **Each can level** (starter, copper, steel, gold, iridium) on the same plot:
   - charging shows the game's charge flashes and sound, about 0.6 s per level (unless "Instant charge" is on);
   - exactly the planned tiles get wet;
   - the stamina used matches the table above at farming level 0.
2. **Stamina:**
   - start an iridium run at about 15 stamina: one big use, then a smaller one, then "Tired of Watering", ending at 1;
   - at 2.9 stamina with a starter can at farming level 0, the bot must not start;
   - the energy bar must never show 0.
3. **Water:** start a copper run with 1 water left. The bot walks to water first, then charges.
4. **Trellises and fences:** a hops or grape row fenced on both long sides is watered from its end; a single fenced-in crop is watered over the fence with a copper or better can.
5. **Ponds:** crops just past a pond tile. The bot must never charge into the pond (that would refill instead of watering).
6. **Interruptions while charging:** press a key (stops cleanly, farmer can move), open a menu (pauses), warp or end the day (cancels).
7. **Settings:** turn "Use charged watering" off (single tiles only), try "Instant charge" and "Optimize for: Speed".
8. **Enchantments**, if you have them: Efficient (no stamina used), Bottomless (no refills), Reaching (5×5 used on wide plots).
9. **Crop under the farmer:** on a plot where the bot has to stand on a crop to charge, that crop must still get watered.

## Known limits

- The facing for a single diagonal tile is still up/down first, so the splash can look misaimed even though the right soil gets wet.
- The planner is greedy. It isn't guaranteed to be optimal, but on the tested layouts it matches or beats the hand-worked numbers.
- Walking between stops is still done by the game's `PathFindController`; a pet standing on a standing tile, or blocking the path for 3 s, still stops the run (open issue #2 in [waterbot-fixes.md](waterbot-fixes.md)).
