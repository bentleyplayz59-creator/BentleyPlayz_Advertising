# Bentley's Advertising

A Stardew Valley SMAPI mod by Bentley Playz.

## Architecture
- One main C# DLL.
- `config.json` stores player settings and is read by SMAPI.
- `advertisements.json` stores advertiser definitions, popup variations, mail variations, and subscription offers.

## Initial source milestone
This commit establishes the SMAPI project, JSON loading, real-time popup scheduling, a custom HUD button and subscription menu, and save-specific subscription state. Advertising mail and expanded business progression checks are planned for the next milestone.

## Build
Build with .NET 6 and Pathoschild.Stardew.ModBuildConfig in an environment with Stardew Valley game files available.
