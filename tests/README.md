# WaterBot regression checks

Run with the .NET 8 SDK:

```sh
dotnet run --project tests/WaterBot.RegressionTests.csproj
```

This executable links the production planner/controller source and uses small game/API doubles. It requires no game installation or test packages. Assertion failures produce a nonzero exit code.

The scenarios cover duplicate diagonal targets, live soil checks, explicit targeting, stale run callbacks, trellis standing positions, unreachable single groups, map edges, movement/animation failures, lifecycle cancellation, resource limits, and both refill settings. An exhaustive sweep also checks all 512 patterns of a 3×3 crop patch in both grouping modes, mixing trellises and walkable crops.

These checks do not exercise the real game's collision, input, animation, or multiplayer engines. Separately build the production project against installed game and SMAPI assemblies to catch API compatibility errors.

## In-game verification

1. In both grouping modes, water diagonal crops and dense plots with a base and upgraded can. Each dry crop should receive one action; already-watered soil should consume none.
2. Repeat with a controller, hidden mouse cursor, and a scrolled viewport. Check the soil tile affected, not just the direction of the farmer's pouring animation.
3. Test trellis rows, an enclosed crop, and a blocked route. Accessible crops should be watered; an unreachable crop should be reported. A path failure must leave the bot idle and allow another activation.
4. Stop during movement, watering, and refilling, then restart immediately. The old run must not move the farmer, consume water, or restart itself.
5. Empty the can mid-run with path recalculation enabled and disabled. Then test refill on finish: it should refill once and finish without retracing the last crop group.
6. Open a menu during a run, change tools, warp, end the day, and return to the title screen. Menus should pause progress; lifecycle changes should cancel it cleanly.
7. Test low stamina, no reachable refill source, and a bottomless can. Confirm the appropriate exit message and that player movement is restored.
8. Trigger on growing/regrowing crops, then on harvest-ready crops. Growing crops should start the bot; harvest-ready crops should retain the normal harvest interaction.
9. In multiplayer, have another farmer water a queued crop. The bot should skip that target if it is already watered when its tool effect runs.
