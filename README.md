# Bentley's Advertising

A Stardew Valley SMAPI mod by Bentley Playz. **Version 1.0.2.**

Pelican Town's businesses now compete for your attention: randomized advertisement popups appear on a timer, advertising letters turn up in your mailbox, and you can spend gold on temporary ad-free subscriptions.

## Testing

The public 1.0.2 build contains the advertising gameplay, mailbox letters, subscription shop, draggable AD button, and per-save play-time tracking. Development controls and the temporary screenshot collection feature have been removed.

## Installation

1. Install the game and [SMAPI](https://smapi.io) (on Android, use a SMAPI launcher for Android). The mod needs SMAPI 4.3.0 or newer.
2. Download the release ZIP (`BentleyPlayz.Advertising 1.0.2.zip`) and extract it.
3. Copy the `BentleyPlayz.Advertising` folder into your SMAPI `Mods` folder (your launcher shows where that is on Android).
4. Start the game through SMAPI. A welcome screen appears the first time you load a save.

Updating: replace the `BentleyPlayz.Advertising` folder with the new one. Your settings (`config.json`), AD button position and welcome-screen state (`local-state.json`) are not part of the ZIP, so they are kept. Subscriptions and ad timers live inside your save files and carry over.

## Features

- **Randomized advertisement popups** on a timer (five minutes by default). The screen dims and a parchment box with a red ADVERTISEMENT tab shows the advertiser's name and message for a few seconds. Player movement is never blocked, and a popup that fires during a menu or cutscene waits until you can see it.
- **Advertising letters** delivered through the in-game mailbox. Advertisers can be gated behind progression, such as the repaired bus.
- **Multiple message variations** per advertiser, avoiding consecutive repeats where possible.
- **Ad-free subscriptions** bought with in-game gold (see below), split into a **Short Duration** section at the top of the shop and longer subscriptions below. While a subscription is active no popups or letters are generated.
- **Draggable AD button.** Defaults to the bottom-right corner; drag it anywhere on screen with a mouse or a finger. The position is saved and the button always stays fully on screen.
- **First-launch welcome screen** that explains the mod once per installation.
- **Per-save timers.** The countdown to the next ad and your subscription are stored in each save file. Both only count down while you are actually playing: they stop on the title screen and while the game is in the background or paused.
- One DLL; all advertiser text and subscription offers live in `advertisements.json`.

## Controls

| Action | Touch / mouse | Keyboard |
| --- | --- | --- |
| Open the AD menu | Tap or click the gold AD button | `K` (configurable, `OpenMenuKey`) |
| Move the AD button | Press and hold the button, then drag | - |
| Reset the AD button to its corner | AD menu > **Reset AD button** | - |
| Select an offer | Tap it | - |
| Scroll offers | Drag the list or use the arrows | Mouse wheel |
| Buy | Select an offer, tap **Buy**, then confirm | - |

A drag never opens the menu or triggers anything underneath the button.

## Subscriptions

Open the AD menu to see the time until the next ad, your gold, every offer's price and length, and the time left on an active subscription. Every purchase asks for confirmation and shows your gold before and after. If you cannot afford an offer, the menu says exactly how much more gold you need. Buying while already ad-free adds the new time on top.

| Section | Offer | Price | Ad-free for |
| --- | --- | ---: | --- |
| **Short Duration** | 5-Minute Pass | 250g | 5 minutes of play time |
| **Short Duration** | 10-Minute Pass | 500g | 10 minutes of play time |
| **Short Duration** | 15-Minute Pass | 750g | 15 minutes of play time |
| **Short Duration** | 20-Minute Pass | 1,000g | 20 minutes of play time |
| Longer Subscriptions | Quick Break | 5,000g | 1 hour of play time |
| Longer Subscriptions | Extended Break | 12,000g | 3 hours of play time |
| Longer Subscriptions | Long Break | 22,000g | 6 hours of play time |
| Longer Subscriptions | Full Day | 60,000g | 24 hours of play time |
| Longer Subscriptions | Monthly Pass | 250,000g | 28 in-game days |

"Play time" is time you are actually playing; it does not run while the game is closed or in the background. Prices and offers can be changed in `advertisements.json`.

## Configuration

`config.json` is created the first time the game runs with the mod:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Turns the whole mod on or off. |
| `AdvertisementIntervalMinutes` | `5` | Play-time minutes between ads. |
| `EnablePopups` | `true` | Show advertisement popups. |
| `EnableMail` | `true` | Deliver advertising letters. |
| `EnableSubscriptions` | `true` | Show the AD button and subscription shop. |
| `PopupDurationSeconds` | `7` | How long a popup stays (3-30). |
| `PopupDimAmount` | `0.6` | How dark the screen gets behind a popup (0-0.9). |
| `PlayPopupSound` | `true` | Play a sound when a popup appears. |
| `HudButtonSize` | `72` | AD button size in UI pixels (48-128). Larger is easier to tap. |
| `OpenMenuKey` | `K` | Keyboard shortcut for the AD menu (`None` disables it). |

The AD button position and the welcome-screen flag are stored in `local-state.json` (delete it to see the welcome screen again and reset the button).

## Version 1.0.0

First official production release.

- Draggable AD button with mouse and touch support, saved position, on-screen clamping, and a reset option.
- One-time welcome screen, sized for small phone screens.
- Rebalanced subscription prices, a purchase confirmation step, and clearer price and duration text.
- Ad timer and hour-based subscriptions now count play time only. Subscriptions from earlier versions are converted automatically when the save loads.
- Removed development and debug features, including the `[AD-INPUT]` logging.
- Removed obsolete config options (`PopupCorner`, `HudButtonPosition`, `HudButtonOffsetFromRight`, `HudButtonOffsetFromTop`). Old `config.json` files that still contain them keep working; the extra entries are ignored.
- `config.json` is no longer shipped in the ZIP, so updates never overwrite your settings.

## Files

- `manifest.json`: SMAPI metadata.
- `BentleyPlayz.Advertising.dll`: mod logic.
- `advertisements.json`: advertiser copy, unlock conditions and subscription offers.
- `config.json` (created on first run): player settings.
- `local-state.json` (created on first run): AD button position and welcome-screen flag.

## Build

GitHub Actions uses the official Pathoschild SMAPI mod build workflow. It provisions the .NET SDK and Stardew Valley reference assemblies, builds the DLL, creates the release ZIP and uploads it as a workflow artifact. No game DLLs are stored in this repository.
