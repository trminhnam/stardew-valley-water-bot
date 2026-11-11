# WaterBot fixes: changes, build and test notes

Status as of 2026-11-11: the code changes are complete. The regression harness (18 scenarios) passes on .NET 8.0.428. **The mod has not been compiled against the real game and SMAPI, and it has not been tested in the game.** The machine used for development had no SMAPI installed.

## Reported problems

1. Some crops are watered twice and others not at all. Aim looks too high or too low.
2. The bot stopped working in late summer, year 1.
3. Every right-click just says "Watering stopped".
4. Crops get watered twice, which wastes water and stamina.

## Causes found in the original code

| Problem | Cause |
|---|---|
| Watered twice | Groups only link crops that touch on a side, but watering also reaches diagonals. A crop diagonal to another group was watered from that group, then its own group cleared the "already planned" flag and watered it again. The standing tile was queued without checking whether it still needed water. |
| Missed crops | `water()` marked a crop done as soon as the animation started, without checking the soil afterwards. Each next step ran 800 ms later on a background thread instead of the game's update loop. |
| Bad standing tiles | For blocked crops (trellises), the standing tile started as the crop itself and the score was never updated, so the bot could be sent to a tile it can't stand on. |
| "Watering stopped" on every click | The message only appears when the bot thinks it is already running. Runs could stay "active" forever: a path to an unreachable tile failed silently, exceptions during planning or in the delayed callbacks left the flag set, and an old callback could cancel a new run's movement. |
| Edge cases | Grouping read past the map edge. A single group skipped the reachability check. The refill after finishing replayed the last route. The trigger click was also handled by the game. |

There is no season or year check in the code. "Late summer" most likely lines up with a bigger farm or trellis crops (such as hops) creating unreachable tiles, which caused the stuck runs above.

## What changed

### `WaterBot/Framework/WaterBotController.cs` (rewritten)

- The bot is now a state machine (`Idle`, `Planning`, `Ready`, `Moving`, `UsingTool`) advanced once per game tick from `UpdateTicked`. There are no background threads.
- Each run has an ID. Callbacks from an old run are ignored.
- Every step runs inside `guarded()`. Any exception cancels the run, logs the details to the SMAPI console and shows "Watering stopped: could not complete the next action. See SMAPI log."
- Movement failure is detected: no path from the game, the controller being replaced, movement ending early, or no progress for 3 seconds.
- The animation must finish within 5 seconds.
- Watering uses the game's own tool animation (`endUsing`). The actual effect runs through `Farmer.toolOverrideFunction`, which calls `WateringCan.DoFunction` on the exact target tile with tool power 0 (one tile).
- Soil is re-checked before the action and again when the effect fires, so already-watered crops are skipped.
- A crop only counts as done if its soil is wet afterwards. A failed attempt is retried once, then the run stops.
- Refill after finishing now ends the run instead of replaying the route.
- At the end, the bot reports how many crops still need water (unreachable or skipped).
- Bottomless and enchanted cans don't go looking for water.

### Stamina rules

Only watering costs stamina (up to 2 per use, less at higher farming levels). Walking and refilling cost nothing.

The bot only waters while stamina is **above 2**. At 2, 1 or below it stops with "Tired of Watering", so stamina can't reach 0. The lowest it can end up is 1 (from 3).

Checks happen at four points:

1. **Starting the bot.** It won't start at 2 or below.
2. **Before each watering stop.** Checked before walking there, since a tired farmer couldn't water on arrival anyway.
3. **Before retrying a failed watering.** Previously this was unchecked and could take stamina from 2 to 0.
4. **When the watering effect fires.** Checked again in case stamina dropped during the animation.

Refilling is never blocked by stamina. If the can runs dry at low stamina, the bot still refills, then stops before the next watering.

The limit is the `MinStamina` constant in `WaterBotController.cs`.

### `WaterBot/Framework/Map.cs`

- After loading the map, a flood fill from the player marks every reachable tile. Standing tiles must be reachable and not blocked.
- `findFillPath` was rewritten. It picks standing tiles that cover the most remaining crops (including diagonals) and plans each crop once. The "planned" flags are no longer cleared between groups.
- `findGroupings` uses a loop instead of recursion and no longer reads past the map edge.
- The single-group shortcut in `findGroupPath` was removed, so reachability is always checked.
- `getMinimalCoverGroups` (the "small grouping" option) was replaced by a greedy cover built from `findFillPath`. It behaves differently from the algorithm added in PR #11/#12.
- `getClosestRefill` handles the case where the player stands next to the water.

### `WaterBot/Framework/Group.cs`

- `Centroid` now returns a reachable standing tile from `Map.groupStandingPlace`.

### `WaterBot/ModEntry.cs`

- Calls `bot.tick()` every game tick.
- Cancels the run on warp, day end and returning to the title screen.
- Only starts when the player is free and not already using a tool.
- The clicked crop must be dry and growing. Harvest-ready crops keep their normal right-click behaviour.
- The triggering click is suppressed so the game doesn't also handle it.

### Other files

- `WaterBot/i18n/default.json`: new messages `process.failed` and `process.remaining`. Other languages fall back to English for these two.
- `README.md` and `CHANGELOG.md`: updated descriptions.
- `tests/`: new standalone regression harness (see below).

## Building and testing on another computer

### Requirements

- .NET 8 SDK (for the test harness). The mod itself targets `net6.0`; the .NET 8 SDK can build it.
- Stardew Valley 1.6 with **SMAPI installed into the game folder**. The mod's build package (`Pathoschild.Stardew.ModBuildConfig` 4.1.1) refuses to build without it.

### 1. Run the regression harness

```sh
dotnet run --project tests/WaterBot.RegressionTests.csproj
```

Expected: `18 regression scenarios passed.` The only warning is the old one about the lowercase `console` type name.

These tests use simplified stand-ins for the game, not the real game. They check the planner and the bot's state handling, but not real collisions, pathfinding, input or animations.

### 2. Build the mod against the real game

Without copying it into the game's Mods folder:

```sh
dotnet build WaterBot/WaterBot.csproj -p:EnableModDeploy=false -p:EnableModZip=false
```

Then a normal build, which deploys to `Mods/WaterBot`:

```sh
dotnet build WaterBot/WaterBot.csproj
```

If the game is in a non-standard location, add `-p:GamePath="/path/to/Stardew Valley"`.

Compile errors here would mean a game or SMAPI API doesn't match. The names were checked against the 1.6.15 game DLL (they all exist), but signatures were not.

### 3. Test in the game

The highest-risk item is **1**. The new watering path assumes that `Tool.endUsing` starts the animation, the animation calls `Farmer.useTool`, which runs `toolOverrideFunction`, and that `WateringCan.DoFunction` uses `who.toolPower` to decide how many tiles to water. If any of these is wrong, every watering fails twice and the run stops at the first crop.

1. **Basic watering.** Plant a small plot and run the bot. Each dry crop should get exactly one watering. Check the soil, not just the animation direction.
2. **Diagonal crops.** Plant crops touching only at the corners, in both grouping modes. No crop should be watered twice.
3. **Stamina.**
   - Start at 2 or 1 stamina: the bot must not start ("Tired of Watering").
   - Start at about 7 with many crops: it should water 3 crops and stop at 1.
   - Let the can run dry at low stamina: it should still refill, then stop before the next watering.
   - Stamina must never reach 0.
4. **Trellis crops and unreachable crops.** Trellis rows, a crop fenced in, a blocked route. Reachable crops get watered, and unreachable ones are reported at the end. The bot must not get stuck; another right-click should start it again.
5. **Stopping.** Press any key while moving, watering and refilling, then start again right away. The old run must not move the farmer or use water.
6. **Refills.** Empty the can mid-run with "Redo path on refill" on and off. Then test "Refill on finish": it should refill once and finish without walking the last route again.
7. **Interruptions.** Open a menu (pauses), change tools, warp, end the day, return to title (all cancel cleanly, and the farmer can move afterwards).
8. **Controller and hidden cursor.** Repeat test 1 with a controller and with the viewport scrolled.
9. **Triggering.** Right-click a growing crop (starts), a harvest-ready crop (harvests normally), and an already-watered crop (does nothing).
10. **Multiplayer.** Have another player water a crop the bot planned to water. The bot should skip it.

Watch the SMAPI console during testing. Every early stop logs its reason as an error.

## Known open issues

1. **One failure stops the whole run.** A missing path, a 3-second stall or a crop failing twice cancels everything. It would be friendlier to skip that stop, carry on, and include it in the "still need water" count. Only problems affecting the whole run (player, location or tool changed) should cancel it.
2. **Pets and animals can end a run.** The standing tile is checked for collisions right before moving. A pet or farm animal on that tile, or blocking the path for 3 seconds, ends the run (if the game treats them as obstacles; not confirmed).
3. **The splash can still look misaimed.** The bot still faces up or down for diagonal crops, and faces right when watering its own tile. The correct soil gets wet, but the animation points elsewhere.
4. **Starting is stricter.** Clicking an already-watered crop does nothing and shows no message.
5. **Small grouping algorithm replaced.** Mention this in the pull request.
6. **Minor:**
   - Diagonal crops in different groups now each get their own stop (a little more walking).
   - `Map.findClosestWalkableTile` and `Tile.reset()` are unused.
   - The new changelog entry sits above `v1.2.1`, while `manifest.json` says 1.4.1, and the version was not bumped.
