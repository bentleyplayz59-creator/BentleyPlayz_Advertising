# Bentley's Advertising

A Stardew Valley 1.6 SMAPI mod by Bentley Playz.

## Architecture

- One main C# DLL: `BentleyPlayz.Advertising.dll`.
- `Code/config.json`: player settings (SMAPI reads these into the mod config).
- `Code/advertisements.json`: advertiser definitions, popup variations, mail variations, unlock rules, and subscription offers.

## Current development milestone

- Real-world-time popup interval (default: five minutes).
- Randomized popup selection from advertiser variations.
- A small HUD button that opens the subscription menu.
- Gold purchases for real-time ad-free passes and a 28-in-game-day pass.
- Save-specific subscription state.
- Data/Mail entries generated from the JSON letter definitions.
- Sandy's advertisements are gated behind the bus unlock flag.

## Subscription offers

| Offer | Price | Duration |
|---|---:|---|
| Quick Break | 500g | 1 real-world hour |
| Extended Break | 1,500g | 3 real-world hours |
| Long Break | 3,000g | 6 real-world hours |
| Full Day | 5,000g | 24 real-world hours |
| Monthly Pass | 25,000g | 28 in-game days |

## Build

The GitHub Actions workflow uses .NET 6 and the SMAPI mod build environment. A successful CI build and Android in-game test are still required before this is considered a working release.
