# Bentley's Advertising

A Stardew Valley SMAPI mod by Bentley Playz.

## Features

- Randomized promotional popups on a real-world timer (five minutes by default).
- Multiple message variations per advertiser, with consecutive repeats avoided when possible.
- Randomized advertising letters delivered through the in-game mailbox.
- Advertisers can be gated behind progression conditions such as the repaired bus.
- An in-game HUD button opens the subscription menu.
- Gold purchases pause ads for a real-world duration or a number of in-game days.
- One main DLL; advertiser copy and variations live in `advertisements.json`.

## Files

- `manifest.json`: SMAPI metadata.
- `config.json`: player settings.
- `advertisements.json`: advertiser copy, unlock conditions, and subscription offers.
- `BentleyPlayz.Advertising.dll`: mod logic.

## Build

GitHub Actions uses the official Pathoschild SMAPI mod build workflow. It provisions the .NET SDK and Stardew Valley reference assemblies automatically, builds the DLL, creates a release ZIP, and uploads it as a workflow artifact. No game DLLs need to be uploaded to this repository.

## Current testing note

A successful CI build confirms compilation and packaging, but the mod still needs an in-game Android test for UI layout, mailbox delivery, and progression behavior.
