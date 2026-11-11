 <p align="center">
  <img src="https://raw.githubusercontent.com/andyruwruw/stardew-valley-water-bot/main/documentation/cover-image.gif">
 </p>
 
 <p align="center">
  <a href="https://www.nexusmods.com/stardewvalley/mods/8167">Download on Nexus</a>
  ·
  <a href="https://github.com/andyruwruw/stardew-valley-water-bot/issues/new/choose">Submit an Issue</a>
  ·
  <a href="https://www.youtube.com/watch?v=_8lDC51DbRs&feature=youtu.be">Youtube Video</a>
</p>
 
 <p align="center">
  For my fellow lazy farmers without sprinklers
 </p>

# WaterBot

**WaterBot** is a [Stardew Valley](https://www.stardewvalley.net/) mod that helps you water your crops while staying as vanilla as possible.

Unlike other mods, watering your crops will still cost you the same amount of time and energy as if you had done it yourself, just a little less tedious.

When you **right-click** a not fully grown plant with your **Watering Can**, the bot will take control of your character, **watering all your crops** and **refilling the water** can at the nearest water source whenever necessary.

**Press any button** at any point to stop the bot. The bot will automatically stop if you have low stamina.

# Contents

- [Install](#install)
- [How to Use](#how-to-use)
- [Implementation](#implementation)
  1. [Trigger](#1-trigger)
  2. [Loading the Player's Farm](##2-loading-the-players-farm)
  3. [Planning the Route](#3-planning-the-route)
  4. [Watering](#4-watering)
  5. [Out of Water!](#5-out-of-water)

# Install

1. Install the latest version of [SMAPI](https://smapi.io/).
2. Download this mod and unzip the contents.
3. Place the mod in your Mods folder.
4. Run the game using SMAPI.

# How to Use

Good morning!

Time to water your crops.

Pull out your **Watering Can**, and use your **action button** (normally right-click) on a growing crop. Crops ready to harvest keep their normal action-button behavior.

By a miracle of love for your budding crops, you're carried away into watering the rest of them without pushing so much as a button.

You automatically get more water to refill your watering can from instinct, still in a lucid state.

Snap out of it early by pressing any button.

Don't worry, you won't knock yourself out from watering, you will stop before you run out of stamina

With an upgraded watering can, the bot charges it like you would (holding for 0.6 s per level) and waters whole lines and squares of crops at once wherever that saves stamina. It can even water trellis rows from the end and crops behind a fence.

## Settings

With [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) installed, these can be changed in game; otherwise edit `config.json`.

| Setting | Default | What it does |
|---|---|---|
| `UseChargedWatering` | `true` | Use an upgraded can's lines and squares. Off: one tile at a time. |
| `InstantCharge` | `false` | Skip the hold time when charging. |
| `Optimize` | `Stamina` | `Stamina`: least stamina per crop. `Speed`: least time per crop. |
| `RefillOnFinish` | `false` | Refill the can at the end of a run. |
| `RefillIfLower` | `95` | With `RefillOnFinish`, only refill below this percentage. |
| `RedoPathOnRefill` | `false` | Plan the route again after each refill. |

# Implementation

### 1. Trigger

To begin the mod listens for whenever the player `right-clicks`. If the player is clicking a crop with their watering can, the bot starts.

### 2. Loading the Player's Farm

The bot first looks through the farm map data, going tile by tile and marking the following traits:

| Trait     | Description                                         |
|-----------|-----------------------------------------------------|
| Waterable | Does the tile need to be watered                    |
| Block     | Does the tile allow the player to walk on top of it |
| Water     | Can the player refill the Watering Can here         |

All the tiles are placed in a 2D array. Any waterable crops are also placed in their own array.

<img src="https://raw.githubusercontent.com/andyruwruw/stardew-valley-water-bot/main/documentation/implementation/load_map.gif">

For the image above, the tiles are colors accordingly:

| Color      | Waterable | Block | Water |
|------------|-----------|-------|-------|
| Green      | True      | False | False |
| Dark Green | True      | True  | False |
| Turquoise  | False     | True  | True  |
| Dark Blue  | False     | False | True  |
| Red        | False     | True  | False |

### 3. Planning the Route

A breadth-first search from the player marks every tile the player can walk to. Every step costs the same, so this also gives the exact walking distance to each tile, going around fences, buildings and trellis crops.

The bot then plans the run in two steps:

1. **Choose the can uses.** It lists every way to water each dry crop: one tile at a time from any reachable tile next to it, and, with an upgraded can, every charged pattern (1×3 and 1×5 lines, 3×3, 3×6 and with Reaching 5×5) in every direction that includes it. Patterns reach over fences and trellises; they never start on a water tile, because the game would refill the can instead. It then repeatedly takes the use that waters the most still-dry crops per stamina point, so big patterns are used where they're full and smaller ones or single tiles for the leftovers.
2. **Order the stops** by real walking distance: nearest stop next, then a 2-opt pass that removes crossings.

A charged pattern never waters the tile the farmer stands on. If that tile is a dry crop, it gets a single-tile watering at the same stop.

Crops nothing can reach are skipped and reported when the run ends. Planning a farm of 480 crops takes about 0.1–0.2 s.

### 4. Watering

Each crop is queued once, and the bot checks the live soil again before using the can, so crops already watered by another player or action are skipped.

Watering uses the normal can animation and charging effects, with an explicit target tile and charge level. The bot waits for the animation to finish and checks every target crop afterwards. A crop that is still dry is tried once more, then skipped and reported at the end, and the run carries on. Missing paths, stalled movement and other problems that affect the whole run stop it with a message and details in the SMAPI log.

**Stamina.** Watering costs `2 × (charge level + 1) − 0.1 × farming level` (nothing with the Efficient enchantment). The bot rounds that cost up and your stamina down, as the energy bar shows it, and only waters if at least 1 whole point is left afterwards, so you can't be knocked out. When stamina runs low it plans the rest with smaller patterns before stopping. Walking and refilling cost no stamina.

**Water.** A charged use takes `charge level + 1` water. The bot refills before any use that needs more than is left.

### 5. Out of Water!

When the watering can is low, the bot will go to the nearest source of water to refill.

The closest refillable spot is found using breadth-first-search from the player's position.

Once the spot is found, the bot navigates the player to the closest spot to the water, refills, then returns to watering.

<img src="https://raw.githubusercontent.com/andyruwruw/stardew-valley-water-bot/main/documentation/implementation/refill_water.gif">

# Development checks

Build the mod with `dotnet build WaterBot/WaterBot.csproj` after installing Stardew Valley and SMAPI. To build without deploying into the game, pass `-p:EnableModDeploy=false -p:EnableModZip=false`.

Run the standalone regression harness with `dotnet run --project tests/WaterBot.RegressionTests.csproj` (.NET 8 SDK). See [tests/README.md](tests/README.md) for its scope and the in-game verification checklist.
