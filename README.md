# Bentley's Advertising

A Stardew Valley SMAPI mod by Bentley Playz.

## Features

- Randomized promotional popups on a real-world timer (five minutes by default).
- Popups dim the whole screen and show a large parchment box with a red ADVERTISEMENT tab, the advertiser's name and the message for a few seconds. Player movement is never blocked.
- The ad timer is stored in each save file: saving and quitting stops it, and loading that save resumes it where it left off. Each save has its own timer and its own subscription.
- Multiple message variations per advertiser, with consecutive repeats avoided when possible.
- Randomized advertising letters delivered through the in-game mailbox.
- Advertisers can be gated behind progression conditions such as the repaired bus.
- A gold "AD" button at the top-center of the screen opens the subscription menu (optional keyboard shortcut: `OpenMenuKey`, default `K`).
- The menu shows the time until the next ad, your gold, each offer's price and duration, and the time left on an active subscription (real-time countdown, or in-game days left). Tap an offer to select it, then press Buy.
- Gold purchases pause ads for a real-world duration or a number of in-game days. While a subscription is active no popups or letters are generated.
- One main DLL; advertiser copy and variations live in `advertisements.json`.

## Files

- `manifest.json`: SMAPI metadata.
- `config.json`: player settings (popup length/dimming/sound, AD button position and size, menu key). `HudButtonPosition` can be `TopCenter` (default), `LeftOfClock` or `Custom`.
- `advertisements.json`: advertiser copy, unlock conditions, and subscription offers.
- `BentleyPlayz.Advertising.dll`: mod logic.

## Build

GitHub Actions uses the official Pathoschild SMAPI mod build workflow. It provisions the .NET SDK and Stardew Valley reference assemblies automatically, builds the DLL, creates a release ZIP, and uploads it as a workflow artifact. No game DLLs need to be uploaded to this repository.

## Current testing note

A successful CI build confirms compilation and packaging, but the mod still needs in-game Android testing for the AD button position, subscription menu layout and popup layout. Turn on trace logging in SMAPI to see `[AD-INPUT]` lines for taps near the AD button.
