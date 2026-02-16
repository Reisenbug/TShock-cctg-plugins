# cctgPlugin Development Guide

## Build & Deploy

```bash
# Build the plugin
cd /Users/lhy/Documents/TShockPlugins/ServerPlugins/cctgPlugin
dotnet build

# Copy to TShock runtime directory (tshock-source)
cp bin/Debug/net9.0/cctgPlugin.dll /Users/lhy/Documents/tshock-source/TShock/TShockLauncher/bin/Debug/net9.0/ServerPlugins/
```

## TeamNPCPlugin (related)

```bash
# Build TeamNPCPlugin
cd /Users/lhy/Documents/TShockPlugins/ServerPlugins/TeamNPCPlugin
dotnet build

# Copy to TShock runtime directory
cp bin/Debug/net9.0/TeamNPCPlugin.dll /Users/lhy/Documents/tshock-source/TShock/TShockLauncher/bin/Debug/net9.0/ServerPlugins/
```

## Quick Deploy Both

```bash
cd /Users/lhy/Documents/TShockPlugins/ServerPlugins/cctgPlugin && dotnet build && \
cd /Users/lhy/Documents/TShockPlugins/ServerPlugins/TeamNPCPlugin && dotnet build && \
cp /Users/lhy/Documents/TShockPlugins/ServerPlugins/cctgPlugin/bin/Debug/net9.0/cctgPlugin.dll /Users/lhy/Documents/tshock-source/TShock/TShockLauncher/bin/Debug/net9.0/ServerPlugins/ && \
cp /Users/lhy/Documents/TShockPlugins/ServerPlugins/TeamNPCPlugin/bin/Debug/net9.0/TeamNPCPlugin.dll /Users/lhy/Documents/tshock-source/TShock/TShockLauncher/bin/Debug/net9.0/ServerPlugins/
```

## Project Structure

- **cctgPlugin**: Main game plugin (houses, boundaries, teleport, items, PVP control)
- **TeamNPCPlugin**: Team-based NPC management (reads house positions from cctgPlugin via reflection)

## Code Style

- 不要添加或修改注释，除非我明确要求。

## Key Files

- `HouseBuilder.cs` - House construction coordinator
- `HouseLocationFinder.cs` - Finds flat ground 150-350 tiles from spawn
- `HouseStructure.cs` - Builds house rooms and furniture
- `CctgPlugin.cs` - Main plugin entry, commands (/start, /end, etc.), PVP/time events
