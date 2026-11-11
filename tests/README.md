# WaterBot regression checks

Run with the .NET 8 SDK:

```sh
dotnet run --project tests/WaterBot.RegressionTests.csproj
```

This executable links the production planner/controller source and uses small game/API doubles. It requires no game installation or test packages. Assertion failures produce a nonzero exit code.

The scenarios cover duplicate diagonal targets, live soil checks, explicit targeting, stale run callbacks, trellis standing positions, unreachable crops, map edges, movement/animation failures, lifecycle cancellation, stamina limits, and both refill settings. Planner checks cover watering two rows from the path between them, ordering stops by walking distance around a wall, and planning a 480-crop farm in under 2 seconds (it takes about 0.1 s). An exhaustive sweep also checks all 512 patterns of a 3×3 crop patch, mixing trellises and walkable crops.

Upgraded-can checks: the mod's charged patterns are compared with a transcription of the game's `Tool.tilesAffected`; a 3×12 bed at every can level (expected at most 36/12/9/4/2 uses and 72/48/48/32/20 stamina); a gold can choosing the 1×5 line on a single row; trellis rows from the end; watering over a fence; fenced plots where the farmer must stand on crops; stamina rounding; lowering the charge level as stamina runs out; refilling before a charged use; the Efficient and Reaching enchantments; realistic vs instant charging; stopping or being interrupted while charging; never starting a pattern on water; the on/off and Speed settings; and planning 480 crops with an iridium can.

These checks do not exercise the real game's collision, input, animation, or multiplayer engines. Separately build the production project against installed game and SMAPI assemblies to catch API compatibility errors.

## In-game verification

1. Water diagonal crops and dense plots with a base and upgraded can. Each dry crop should receive one action; already-watered soil should consume none.
2. Repeat with a controller, hidden mouse cursor, and a scrolled viewport. Check the soil tile affected, not just the direction of the farmer's pouring animation.
3. Test trellis rows, an enclosed crop, and a blocked route. Accessible crops should be watered; an unreachable crop should be reported. A path failure must leave the bot idle and allow another activation.
4. Stop during movement, watering, and refilling, then restart immediately. The old run must not move the farmer, consume water, or restart itself.
5. Empty the can mid-run with path recalculation enabled and disabled. Then test refill on finish: it should refill once and finish without retracing the last crop group.
6. Open a menu during a run, change tools, warp, end the day, and return to the title screen. Menus should pause progress; lifecycle changes should cancel it cleanly.
7. Test low stamina, no reachable refill source, and a bottomless can. Confirm the appropriate exit message and that player movement is restored.
8. Trigger on growing/regrowing crops, then on harvest-ready crops. Growing crops should start the bot; harvest-ready crops should retain the normal harvest interaction.
9. In multiplayer, have another farmer water a queued crop. The bot should skip that target if it is already watered when its tool effect runs.
10. Upgraded cans: see the per-can checklist in [docs/upgraded-cans.md](../docs/upgraded-cans.md).
