# Watering mechanics confirmed in the game code

Source: `Stardew Valley.dll` 1.6.15 (build 24356), decompiled with ILSpy (`ilspycmd` 9.1) on 2026-11-11. Method names are given so they can be re-checked after a game update.

## Charge levels and tile patterns (`Tool.tilesAffected`)

The pattern starts at the tile passed to `DoFunction` (the "origin"; for a player, the tile in front) and points the way the farmer faces (`who.FacingDirection`). Power `p` is `Farmer.toolPower`.

| p | Shape (forward × sideways, from the origin) | Tiles |
|---|---|---|
| 0 | origin only | 1 |
| 1 | forward 0–2 | 3 |
| 2 | forward 0–4 | 5 |
| 3 | forward 0–2 × sideways −1..1 | 9 |
| 4 | forward 0–5 × sideways −1..1 | 18 |
| 5 | forward 0–4 × sideways −2..2 (5×5 centred 2 tiles ahead) | 25 |

- **No obstacle checks.** The list of tiles is built from geometry alone, so patterns reach over fences, trellises and buildings.
- `WateringCan.DoFunction` ignores its `power` argument and uses `who.toolPower.Value`.

## Maximum charge level and charge time (`Game1` input loop, `Farmer.toolPowerIncrease`)

- Maximum level: `CurrentTool.upgradeLevel + (has ReachingToolEnchantment ? 1 : 0)`. Starter 0, Copper 1, Steel 2, Gold 3, Iridium 4, Iridium + Reaching 5.
- Each level takes `600 ms × CurrentTool.AnimationSpeedModifier` of holding (`Game1.toolHoldPerPowerupLevel = 600`).
- Charging only continues while `player.Stamina >= 1`.
- `Farmer.toolPowerIncrease()` raises `toolPower` by 1 and plays the charge effects (coloured flash, `toolCharge` sound, farmer shake via `jitterStrength`).

## Stamina, water and refills (`WateringCan.DoFunction`)

- **Refill first:** if the origin tile can refill the can (`CanRefillWateringCanOnTile`), the can is refilled to `waterCanMax` and **nothing is watered and no stamina is used**, whatever the charge level. A charged pattern must never start on a water tile.
- Otherwise, if `WaterLeft > 0` or the farmer has `hasWateringCanEnchantment`:
  - Stamina: `Stamina -= 2 × (p + 1) − FarmingLevel × 0.1`, unless the can `IsEfficient` (Efficient enchantment), which costs nothing.
  - Every tile in the pattern is watered (`HoeDirt.performToolAction` sets `state = 1`).
  - Water: `WaterLeft -= p + 1`, unless `IsBottomless`. The game doesn't stop this going below 0 (e.g. 1 water left with p = 4 gives −4); the bot never relies on that.
- With an empty can (and no enchantment), nothing happens and no stamina is used.
- `FarmingLevel` = base level + food buffs (never below 0).
- Can capacity (`waterCanMax`): 40 / 55 / 70 / 85 / 100 for levels 0–4. A bottomless can always reports full.
- `Farmer.Stamina` is a float, clamped to −16 … `MaxStamina`.

## Energy bar rounding (`Game1.drawHUD`)

The number shown is `(int)Math.Max(0, Stamina)`, so stamina is **rounded down** for display. 2.9 shows as 2, 0.5 shows as 0. The bot therefore rounds stamina down and costs up before deciding whether it can water.

## Watering animation and the bot's tool hook

- `Tool.endUsing` (for a `WateringCan`) plays `FarmerSprite.animateOnce(164/172/180/188, 125 ms, 3)`, depending on facing.
- Those animations (`FarmerSprite`) are: frame 0 → 75 ms `Farmer.showToolSwipeEffect` → 100 ms `Farmer.useTool` → 500 ms `Farmer.canMoveNow`. About 0.68 s per watering.
- `Farmer.useTool` calls `who.toolOverrideFunction(who)` when it is set, instead of the default `DoFunction`. This is the hook the bot uses to apply the effect on its exact target tile.
- `Tool.endUsing` replaces `lastClick` with the mouse/controller tool location when a gamepad button is held. The bot doesn't depend on `lastClick` because of the hook above.
- `Tool.beginUsing` ends the tool use immediately for a starter can (`upgradeLevel <= 0`); the bot does its own charging instead of calling it.
