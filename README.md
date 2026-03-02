# TShock CCTG Plugins

A collection of TShock plugins for the **Capture the Gem** game mode, originally created by **PenguinGames** as a personal interest project.

## Plugins

### cctgPlugin
The core game plugin. Manages the full game cycle including:
- House generation via TEditSch schematics (placed on both sides of spawn)
- Gem lock placement and capture mechanics
- Team-based PVP, boundary enforcement, and sudden death mode
- World queue and automatic world generation/rotation
- Player teleportation, recall potion interception, and respawn handling
- Hook/mount drop on death and recovery at death location
- Day/night cycle events and time-based rule transitions

### TeamNPCPlugin
Manages team-based town NPCs. Each team gets their own set of NPCs spawned near their house, with homes locked to their side of the map.

### WorldGenPlugin
Generates new worlds in a background subprocess without interrupting the running server. Worlds are queued and rotated automatically by cctgPlugin.

### MapChangePlugin
Handles world swapping.

### EventBlocker
Blocks vanilla Terraria invasion events during gameplay.

### HouseSidePlugin
Utility plugin for house side detection.

## Requirements

- [TShock](https://github.com/Pryaxis/TShock) for Terraria 1.4.5
- TEditSch schematic files placed in the `tshock/` directory:
  - `cctgredbase.TEditSch` — Red team house
  - `cctgbluebase.TEditSch` — Blue team house
  - `cctgredgem.TEditSch` — Red team gem lock structure
  - `cctgbluegem.TEditSch` — Blue team gem lock structure

## Permissions

Grant `cctg.admin` permission to allow players to use admin commands (`/start`, `/end`, `/n`, etc.) and change teams:

```
/group addperm <groupname> cctg.admin
```

## Notes

This is a personal interest project. The game mode and plugin logic are original work by PenguinGames.
