# Bentley's Advertising

A Stardew Valley SMAPI mod by Bentley Playz.

## Features

- Randomized promotional popups on a real-world timer (five minutes by default).
- Popups dim the whole screen and show a large parchment box with a red ADVERTISEMENT tab, the advertiser's name and the message for a few seconds. Player movement is never blocked.
- Multiple message variations per advertiser, with consecutive repeats avoided when possible.
- Randomized advertising letters delivered through the in-game mailbox.
- Advertisers can be gated behind progression conditions such as the repaired bus.
- A gold "AD" button next to the in-game clock opens the subscription menu (optional keyboard shortcut: `OpenMenuKey`, default `K`).
- In the menu, tap an offer to select it, then press Buy. The menu shows each offer's price and duration, your gold, and the time left on an active subscription (real-time countdown, or in-game days left).
- Gold purchases pause ads for a real-world duration or a number of in-game days. While a subscription is active no popups or letters are generated.
- One main DLL; advertiser copy and variations live in `advertisements.json`.

## Files

- `manifest.json`: SMAPI metadata.
- `config.json`: player settings (popup length/dimming/sound, AD button size and position, debug timer, menu key).
- `advertisements.json`: advertiser copy, unlock conditions, and subscription offers.
- `BentleyPlayz.Advertising.dll`: mod logic.

## Build

GitHub Actions uses the official Pathoschild SMAPI mod build workflow. It provisions the .NET SDK and Stardew Valley reference assemblies automatically, builds the DLL, creates a release ZIP, and uploads it as a workflow artifact. No game DLLs need to be uploaded to this repository.

## Current testing note

A successful CI build confirms compilation and packaging, but the mod still needs an in-game Android test for the AD button tap, the subscription menu layout/scrolling, popup layout and mailbox delivery. With `ShowDebugTimer` on, taps near the AD button are logged as `[AD-INPUT]` lines in the SMAPI log to help diagnose touch problems.
