# Unreleased

- Prevent duplicate watering and recheck live soil before each tool effect.
- Target the intended tile explicitly, including with a controller or hidden cursor.
- Run navigation and tool actions in the game update loop, with verified completion and bounded retries.
- Stop safely on missing paths, stalled movement, failed animations, and planning errors.
- Ignore stale tool callbacks after cancellation or restarting; reset on warps, day end, and return to title.
- Choose reachable standing positions only, including beside trellis crops.
- Fix reading past the map edge and finish immediately after a completion refill.
- Report crops left unreachable and preserve normal harvesting when triggering the bot.
- Never water at 2 stamina or less; walking and refilling are not limited by stamina.
- Plan the whole farm in one pass using real walking distances: no more multi-second freezes on large farms, and about half the walking of the old default mode.
- Remove the "Use small grouping" option; the single planner replaces both modes.
- Upgraded watering cans: charge like a player and water lines (1×3, 1×5), squares (3×3), 3×6 and 5×5 (Reaching) where that saves stamina, including trellis rows from the end and crops behind fences.
- Stamina checks round the cost up and current stamina down; low stamina lowers the charge level before the bot stops.
- Refill before any charged use that needs more water than is left.
- A crop that stays dry is retried once, then skipped and reported; the run no longer stops for it.
- New settings: "Use charged watering", "Instant charge", "Optimize for" (Stamina or Speed).

# v1.2.1

🐛 Bug Fix

- Fixed bug when trying to water after all crops are already watered.

# v1.2.0

🚀 New Feature

- Translations for different languages added.

🐛 Bug Fix

- Fixed bug when calculating best path through groups

# v1.1.1

🚀 New Feature

- Bot now works on any location (Greenhouse included).
- Any action button set in options now works, not just right-click.

🐛 Bug Fix

- Fixed bug where groups of only unwalkable crops broke the bot.

💅 Polish

- If all crops are grouped, skips TSP algorithm.

# v1.1.0

🚀 New Feature

- Doesn't start bot when harvesting plants and accidentally holding Watering Can.
- Doesn't water crops that are ready to harvest, unless they are crops that regrow after harvest.
- Refills water instantly if needed, instead of walking to the first group at the start.
- Update keys linked to Nexus and Github.

🐛 Bug Fix

- Works with all Watering Can upgrades.
- Correctly finds standing place next to refillable tile.
- Finding the nearest refillable tile should and take less memory.
- Checks for accidental empty groupings.
- Now exits task if now suitable refill tile is reachable.

💅 Polish

- Removal of unnecessary imports.
- Provided console feedback on major events.

# v1.0.0

🚀 New Feature

- Waters your crops upon right-clicking a crop.
- Refills Watering Can when nessisary.
