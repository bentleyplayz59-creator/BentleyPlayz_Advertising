using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BentleyPlayz.Advertising;

public sealed class ModEntry : Mod
{
    private const string MailPrefix = "BentleysAdvertising_";

    private ModConfig Config = new();
    private AdvertisementData Ads = new();
    private readonly Stopwatch AdClock = new();
    private readonly Dictionary<string, string> DynamicMail = new();
    private string? CurrentAdvertiser;
    private string? CurrentMessage;
    private string? LastAdvertiserId;
    private double PopupSecondsRemaining;
    private double PopupTotalSeconds;
    private bool PopupPending;
    private Rectangle HudButton;
    private SubscriptionState Subscription = new();

    /// <summary>Seconds already counted toward the next ad before the stopwatch was last (re)started, e.g. restored from a save.</summary>
    private double AdBaseSeconds;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        LoadAdvertisements();

        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.Display.RenderedHud += OnRenderedHud;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Content.AssetRequested += OnAssetRequested;

        Monitor.Log("Bentley's Advertising initialized.", LogLevel.Info);
    }

    /*********
    ** Data loading
    *********/
    private void LoadAdvertisements()
    {
        string path = Path.Combine(Helper.DirectoryPath, "advertisements.json");
        try
        {
            if (!File.Exists(path))
            {
                Monitor.Log("advertisements.json was not found. No advertisements will be shown.", LogLevel.Warn);
                Ads = new AdvertisementData();
                return;
            }

            string json = File.ReadAllText(path);
            Ads = JsonSerializer.Deserialize<AdvertisementData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AdvertisementData();

            DynamicMail.Clear();
            foreach (Advertiser advertiser in Ads.Advertisers)
            {
                for (int i = 0; i < advertiser.MailVariations.Count; i++)
                {
                    MailVariation mail = advertiser.MailVariations[i];
                    string id = GetMailId(advertiser.Id, i);
                    string text = BuildMailText(mail);
                    DynamicMail[id] = text;

                    // Vanilla letters comfortably fit one page at roughly this length. Longer text risks the
                    // signature being pushed onto a second page.
                    int visibleLength = text.Replace("^", "").Length;
                    if (visibleLength > 260)
                        Monitor.Log($"Letter {id} is {visibleLength} characters long and may split across pages.", LogLevel.Warn);
                }
            }

            Monitor.Log($"Loaded {Ads.Advertisers.Count} advertisers, {DynamicMail.Count} letters and {Ads.SubscriptionOffers.Count} subscription offers.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Ads = new AdvertisementData();
            DynamicMail.Clear();
            Monitor.Log($"Couldn't load advertisements.json: {ex}", LogLevel.Error);
        }
    }

    private static string GetMailId(string advertiserId, int variationIndex) =>
        $"{MailPrefix}{advertiserId}_{variationIndex + 1}";

    /// <summary>
    /// Converts a letter from advertisements.json into Stardew's Data/mail format.
    /// Vanilla uses '^' for line breaks and "[#]Title" for the letter title (the title is NOT part of the body).
    /// </summary>
    private static string BuildMailText(MailVariation mail)
    {
        string body = (mail.Body ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

        // Tolerate literal "\n" text (one or more backslashes followed by n) left over from hand-edited JSON.
        body = Regex.Replace(body, @"\\+n", "\n");
        body = Regex.Replace(body, @"[ \t]+\n", "\n");
        body = Regex.Replace(body, @"\n{3,}", "\n\n").Trim();
        body = body.Replace("^", "").Replace("\n", "^");

        string title = (mail.Title ?? "").Replace("^", " ").Replace("[#]", "").Trim();
        return title.Length > 0 ? $"{body}[#]{title}" : body;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
            foreach (KeyValuePair<string, string> mail in DynamicMail)
                data[mail.Key] = mail.Value;
        });
    }

    /*********
    ** Ad timer (per save)
    *********/
    private double GetIntervalSeconds() =>
        Math.Max(1, Config.AdvertisementIntervalMinutes) * 60d;

    private double GetAdElapsedSeconds() =>
        AdBaseSeconds + AdClock.Elapsed.TotalSeconds;

    /// <summary>Starts a fresh countdown to the next ad.</summary>
    private void ResetAdClock()
    {
        AdBaseSeconds = 0;
        AdClock.Restart();
    }

    /*********
    ** Save / lifecycle
    *********/
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // Everything here is stored inside the save file, so each save has its own subscription and ad timer.
        Subscription = Helper.Data.ReadSaveData<SubscriptionState>("subscription-state") ?? new SubscriptionState();
        ResetPopupState();

        // Pick the countdown up where the save left off. The cap gives a short grace period so an ad that was
        // almost due when you saved doesn't pop up the instant the save finishes loading.
        double interval = GetIntervalSeconds();
        double saved = Subscription.AdTimerSeconds ?? 0;
        AdBaseSeconds = Math.Clamp(saved, 0, Math.Max(0, interval - 20));
        AdClock.Restart();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        // The countdown stays stopped while you're on the title screen; the next loaded save sets its own.
        ResetPopupState();
        AdBaseSeconds = 0;
        AdClock.Reset();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        // Remember how far into the countdown we are, so quitting and loading this save resumes from here.
        Subscription.AdTimerSeconds = IsAdFree() ? 0 : GetAdElapsedSeconds();
        Helper.Data.WriteSaveData("subscription-state", Subscription);
    }

    private void ResetPopupState()
    {
        PopupPending = false;
        PopupSecondsRemaining = 0;
        PopupTotalSeconds = 0;
        CurrentAdvertiser = null;
        CurrentMessage = null;
        LastAdvertiserId = null;
    }

    /*********
    ** Popup scheduling
    *********/
    /// <summary>
    /// Popups are drawn on the HUD layer, which is hidden during menus and cutscenes. An ad that fires then
    /// used to count down unseen and expire. Now it waits until the player can actually see it.
    /// </summary>
    private static bool CanShowPopupNow() =>
        Context.IsWorldReady && Game1.activeClickableMenu == null && !Game1.eventUp && Game1.displayHUD;

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled)
            return;

        if (Subscription.RealTimeExpiryUtc is not null && DateTime.UtcNow >= Subscription.RealTimeExpiryUtc.Value)
            Subscription.RealTimeExpiryUtc = null;

        if (Subscription.InGameDayExpiry is not null && GetAbsoluteGameDay() >= Subscription.InGameDayExpiry.Value)
            Subscription.InGameDayExpiry = null;

        if (IsAdFree())
        {
            // Start a fresh interval when the subscription ends, and clear anything on screen.
            ResetAdClock();
            PopupPending = false;
            PopupSecondsRemaining = 0;
            return;
        }

        if (!AdClock.IsRunning)
            AdClock.Start();

        bool canShow = CanShowPopupNow();

        // Only count the popup's lifetime down while it can actually be seen.
        if (PopupSecondsRemaining > 0 && canShow && Game1.currentGameTime is not null)
            PopupSecondsRemaining = Math.Max(0, PopupSecondsRemaining - Game1.currentGameTime.ElapsedGameTime.TotalSeconds);

        if (GetAdElapsedSeconds() >= GetIntervalSeconds())
        {
            ResetAdClock();
            if (Config.EnablePopups)
                PopupPending = true;
        }

        if (PopupPending && canShow && PopupSecondsRemaining <= 0)
        {
            PopupPending = false;
            ShowRandomAdvertisement();
        }
    }

    private bool IsAdFree() =>
        (Subscription.RealTimeExpiryUtc is not null && DateTime.UtcNow < Subscription.RealTimeExpiryUtc.Value) ||
        (Subscription.InGameDayExpiry is not null && GetAbsoluteGameDay() < Subscription.InGameDayExpiry.Value);

    private static int GetAbsoluteGameDay() =>
        (Game1.Date.Year * 112) + (Game1.Date.SeasonIndex * 28) + Game1.Date.DayOfMonth;

    private void ShowRandomAdvertisement()
    {
        List<Advertiser> available = Ads.Advertisers
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName))
            .Where(IsAdvertiserUnlocked)
            .Where(p => p.PopupVariations.Count > 0)
            .ToList();

        if (available.Count == 0)
        {
            Monitor.Log("A popup was due but no unlocked advertiser has popup text.", LogLevel.Warn);
            return;
        }

        List<Advertiser> alternatives = available.Where(p => p.Id != LastAdvertiserId).ToList();
        if (alternatives.Count > 0)
            available = alternatives;

        Advertiser advertiser = available[Game1.random.Next(available.Count)];
        CurrentAdvertiser = advertiser.DisplayName;
        CurrentMessage = advertiser.PopupVariations[Game1.random.Next(advertiser.PopupVariations.Count)];
        LastAdvertiserId = advertiser.Id;
        PopupTotalSeconds = Math.Clamp(Config.PopupDurationSeconds, 3, 30);
        PopupSecondsRemaining = PopupTotalSeconds;
        if (Config.PlayPopupSound)
            Game1.playSound("dwop");
        Monitor.Log($"Showing popup from {advertiser.DisplayName}.", LogLevel.Debug);
    }

    private bool IsAdvertiserUnlocked(Advertiser advertiser) =>
        (advertiser.UnlockCondition ?? "Always").ToLowerInvariant() switch
        {
            "bus" => Game1.MasterPlayer.mailReceived.Contains("ccVault")
                || Game1.MasterPlayer.mailReceived.Contains("jojaVault"),
            "communitycenter" => Game1.MasterPlayer.mailReceived.Contains("ccIsComplete"),
            "mines" or "mine" or "guild" => Game1.MasterPlayer.mailReceived.Contains("guildQuest"),
            _ => true
        };

    /*********
    ** Mail
    *********/
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled || !Config.EnableMail || IsAdFree())
            return;

        List<(Advertiser Advertiser, int Index)> choices = new();
        foreach (Advertiser advertiser in Ads.Advertisers.Where(IsAdvertiserUnlocked))
        {
            for (int i = 0; i < advertiser.MailVariations.Count; i++)
            {
                string mailId = GetMailId(advertiser.Id, i);
                if (Game1.player.mailReceived.Contains(mailId)
                    || Game1.player.mailForTomorrow.Contains(mailId)
                    || Game1.player.mailbox.Contains(mailId))
                    continue;

                choices.Add((advertiser, i));
            }
        }

        if (choices.Count == 0)
            return;

        (Advertiser selected, int variation) = choices[Game1.random.Next(choices.Count)];
        Game1.player.mailForTomorrow.Add(GetMailId(selected.Id, variation));
        Monitor.Log($"Queued an advertising letter from {selected.DisplayName}.", LogLevel.Trace);
    }

    /*********
    ** Status text shown in the AD menu
    *********/
    private static string FormatRemaining(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s";

        return $"{span.Minutes}m {span.Seconds:00}s";
    }

    /// <summary>Time left on whichever subscriptions are active, e.g. "52m 10s left" or "12 in-game days left". Empty when none.</summary>
    private string GetRemainingText()
    {
        List<string> parts = new();

        if (Subscription.RealTimeExpiryUtc is not null && DateTime.UtcNow < Subscription.RealTimeExpiryUtc.Value)
            parts.Add($"{FormatRemaining(Subscription.RealTimeExpiryUtc.Value - DateTime.UtcNow)} left");

        if (Subscription.InGameDayExpiry is not null)
        {
            int days = Subscription.InGameDayExpiry.Value - GetAbsoluteGameDay();
            if (days > 0)
                parts.Add(days == 1 ? "1 in-game day left" : $"{days} in-game days left");
        }

        return string.Join("  +  ", parts);
    }

    /// <summary>"Next ad in 3m 21s". Empty while ad-free (the menu shows the subscription countdown instead).</summary>
    private string GetNextAdText()
    {
        if (IsAdFree())
            return "";

        if (!Config.EnablePopups)
            return "Popup ads are turned off.";

        if (PopupPending)
            return "An ad is waiting - it shows once you close this menu.";

        double remaining = Math.Max(0, GetIntervalSeconds() - GetAdElapsedSeconds());
        return $"Next ad in {FormatRemaining(TimeSpan.FromSeconds(Math.Ceiling(remaining)))}";
    }

    /*********
    ** Input
    *********/
    private bool CanUseHudButton() =>
        Config.Enabled && Config.EnableSubscriptions && Context.IsWorldReady && Game1.activeClickableMenu == null
        && !Game1.eventUp && Game1.displayHUD;

    /// <summary>The tappable area: the drawn button plus a little slack, because fingers are bigger than cursors.</summary>
    private Rectangle GetHudButtonHitArea()
    {
        if (HudButton.Width <= 0 || HudButton.Height <= 0)
            return Rectangle.Empty;

        Rectangle hit = HudButton;
        hit.Inflate(10, 10);
        return hit;
    }

    private void OpenSubscriptionMenu()
    {
        Game1.playSound("bigSelect");
        Game1.activeClickableMenu = new SubscriptionMenu(Ads.SubscriptionOffers, IsAdFree, GetRemainingText, GetNextAdText, PurchaseOffer);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!CanUseHudButton())
            return;

        // Optional keyboard shortcut.
        if (Config.OpenMenuKey != SButton.None && e.Button == Config.OpenMenuKey)
        {
            if (Game1.chatBox is not null && Game1.chatBox.isActive())
                return;

            Helper.Input.Suppress(e.Button);
            OpenSubscriptionMenu();
            return;
        }

        if (e.Button != SButton.MouseLeft && !e.Button.IsUseToolButton())
            return;

        // The button is drawn in UI space, so compare against UI-scaled coordinates. Game1.getMouseX/Y(true) is the
        // same space vanilla menus use for their own clicks; checking both covers any difference between them.
        Point scaled = e.Cursor.GetScaledScreenPixels().ToPoint();
        Point game = new(Game1.getMouseX(true), Game1.getMouseY(true));
        Rectangle hit = GetHudButtonHitArea();
        bool hitButton = hit != Rectangle.Empty && (hit.Contains(scaled) || hit.Contains(game));

        // Only visible in the SMAPI log when trace logging is on; handy if taps ever stop registering.
        Rectangle near = hit;
        near.Inflate(300, 300);
        if (hit != Rectangle.Empty && (near.Contains(scaled) || near.Contains(game)))
        {
            Monitor.Log(
                $"[AD-INPUT] {e.Button} scaled=({scaled.X},{scaled.Y}) game=({game.X},{game.Y}) button={hit} " +
                $"ui={Game1.uiViewport.Width}x{Game1.uiViewport.Height} hit={hitButton}",
                LogLevel.Trace);
        }

        if (!hitButton)
            return;

        Helper.Input.Suppress(e.Button);
        OpenSubscriptionMenu();
    }

    private bool PurchaseOffer(SubscriptionOffer offer)
    {
        if (!Config.EnableSubscriptions)
            return false;

        if (offer.DurationMinutes <= 0 && offer.DurationInGameDays <= 0)
        {
            Monitor.Log($"Offer '{offer.Id}' has no duration, so it can't be purchased.", LogLevel.Warn);
            return false;
        }

        if (Game1.player.Money < offer.Price)
        {
            Game1.addHUDMessage(new HUDMessage("You don't have enough gold.", HUDMessage.error_type));
            return false;
        }

        Game1.player.Money -= offer.Price;

        if (offer.DurationMinutes > 0)
        {
            DateTime start = Subscription.RealTimeExpiryUtc is not null && Subscription.RealTimeExpiryUtc.Value > DateTime.UtcNow
                ? Subscription.RealTimeExpiryUtc.Value
                : DateTime.UtcNow;
            Subscription.RealTimeExpiryUtc = start.AddMinutes(offer.DurationMinutes);
        }

        if (offer.DurationInGameDays > 0)
        {
            int today = GetAbsoluteGameDay();
            int start = Subscription.InGameDayExpiry is not null && Subscription.InGameDayExpiry.Value > today
                ? Subscription.InGameDayExpiry.Value
                : today;
            Subscription.InGameDayExpiry = start + offer.DurationInGameDays;
        }

        PopupPending = false;
        PopupSecondsRemaining = 0;
        Game1.addHUDMessage(new HUDMessage($"{offer.DisplayName} purchased!", HUDMessage.achievement_type));
        return true;
    }

    /*********
    ** Drawing
    *********/
    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled || !Game1.displayHUD)
            return;

        SpriteBatch b = e.SpriteBatch;

        // Keep each element isolated so one failure can never hide the other.
        // Order matters: the popup dims the screen first, then the AD button is drawn on top of the dimming
        // (so it stays visible and tappable during an ad).
        SafeDraw(() => DrawAdvertisementPopup(b), "advertisement popup");
        SafeDraw(() => DrawHudButton(b), "AD button");
    }

    private bool LoggedDrawError;

    private void SafeDraw(Action draw, string what)
    {
        try
        {
            draw();
        }
        catch (Exception ex)
        {
            if (LoggedDrawError)
                return;
            LoggedDrawError = true;
            Monitor.Log($"Couldn't draw the {what}: {ex}", LogLevel.Error);
        }
    }

    /// <summary>
    /// One rectangle is used for both drawing and clicking, so the two can't drift apart.
    /// Default is top-center of the screen, which none of the vanilla or Android HUD elements use.
    /// </summary>
    private Rectangle ComputeHudButtonBounds()
    {
        int size = Math.Clamp(Config.HudButtonSize, 48, 128);
        int viewW = Game1.uiViewport.Width;
        int x = (viewW - size) / 2;
        int y = Config.HudButtonOffsetFromTop;

        string mode = (Config.HudButtonPosition ?? "TopCenter").Trim();
        if (mode.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            x = viewW - Config.HudButtonOffsetFromRight;
        }
        else if (mode.Equals("LeftOfClock", StringComparison.OrdinalIgnoreCase) && Game1.dayTimeMoneyBox is not null)
        {
            int clockLeft = Game1.dayTimeMoneyBox.xPositionOnScreen;
            int clockTop = Game1.dayTimeMoneyBox.yPositionOnScreen;
            if (clockLeft > size + 24 && clockLeft < viewW)
            {
                x = clockLeft - size - 16;
                y = Math.Max(clockTop, 8) + 12;
            }
        }

        x = Math.Clamp(x, 8, Math.Max(8, viewW - size - 8));
        y = Math.Clamp(y, 8, Math.Max(8, Game1.uiViewport.Height - size - 8));
        return new Rectangle(x, y, size, size);
    }

    private void DrawHudButton(SpriteBatch b)
    {
        HudButton = ComputeHudButtonBounds();

        if (!CanUseHudButton())
            return;

        Rectangle area = HudButton;
        bool adFree = IsAdFree();
        bool hovered = GetHudButtonHitArea().Contains(Game1.getMouseX(true), Game1.getMouseY(true));

        // Drop shadow so the button doesn't melt into the HUD.
        b.Draw(Game1.staminaRect, new Rectangle(area.X + 4, area.Y + 6, area.Width, area.Height), Color.Black * 0.35f);

        Color tint = adFree ? Color.LightGreen : hovered ? Color.Yellow : Color.Gold;
        StardewValley.Menus.IClickableMenu.drawTextureBox(
            b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            area.X, area.Y, area.Width, area.Height, tint, 4f, false);

        const string label = "AD";
        Vector2 size = Game1.dialogueFont.MeasureString(label);
        Utility.drawTextWithShadow(b, label, Game1.dialogueFont,
            new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f),
            adFree ? Color.DarkGreen : new Color(140, 30, 20));
    }

    private static void DrawCentered(SpriteBatch b, string text, SpriteFont font, float centerX, float y, Color color)
    {
        Vector2 size = font.MeasureString(text);
        Utility.drawTextWithShadow(b, text, font, new Vector2(centerX - size.X / 2f, y), color);
    }

    /// <summary>
    /// A deliberately loud interruption: the whole view dims and a big parchment box with a red ADVERTISEMENT tab
    /// appears in the middle of the screen. Movement is NOT blocked - the player keeps control throughout.
    /// </summary>
    private void DrawAdvertisementPopup(SpriteBatch b)
    {
        if (PopupSecondsRemaining <= 0 || CurrentMessage is null || IsAdFree() || !CanShowPopupNow())
            return;

        int viewW = Game1.uiViewport.Width;
        int viewH = Game1.uiViewport.Height;

        // Quick fade in, short fade out.
        double total = Math.Max(0.5, PopupTotalSeconds);
        double elapsed = total - PopupSecondsRemaining;
        float fade = (float)Math.Clamp(Math.Min(elapsed / 0.25, PopupSecondsRemaining / 0.35), 0d, 1d);

        // 1) Dim the whole game view so the ad really interrupts what the player is looking at.
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, viewW, viewH),
            Color.Black * (Math.Clamp(Config.PopupDimAmount, 0f, 0.9f) * fade));

        // 2) Work out the box. Big fonts first; fall back to the small font if the screen is too short.
        int boxWidth = Math.Min(920, viewW - 32);
        int pad = viewW < 700 ? 24 : 40;
        int textWidth = boxWidth - pad * 2;
        int bannerHeight = Game1.smallFont.LineSpacing + 14;
        int footerHeight = Game1.smallFont.LineSpacing + 22;

        SpriteFont headingFont = Game1.dialogueFont;
        SpriteFont bodyFont = Game1.dialogueFont;
        string heading = "";
        string[] lines = Array.Empty<string>();
        int boxHeight = 0;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            heading = SubscriptionMenu.TrimToWidth(CurrentAdvertiser ?? "ADVERTISEMENT", headingFont, textWidth);
            lines = Game1.parseText(CurrentMessage, bodyFont, textWidth)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Take(4)
                .ToArray();

            boxHeight = pad + bannerHeight / 2 + headingFont.LineSpacing + 16
                + lines.Length * bodyFont.LineSpacing + 16 + footerHeight + pad;

            if (boxHeight + bannerHeight / 2 <= viewH - 16)
                break;

            headingFont = Game1.smallFont;
            bodyFont = Game1.smallFont;
        }

        boxHeight = Math.Min(boxHeight, viewH - bannerHeight / 2 - 16);
        int totalHeight = boxHeight + bannerHeight / 2;
        int top = Math.Max(8, (viewH - totalHeight) / 2);
        int boxX = (viewW - boxWidth) / 2;
        int boxY = top + bannerHeight / 2;
        float centerX = viewW / 2f;

        // 3) Parchment box.
        StardewValley.Menus.IClickableMenu.drawTextureBox(
            b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), boxX, boxY, boxWidth, boxHeight, Color.White * fade);

        // 4) Red "ADVERTISEMENT" tab straddling the top edge, so it's unmistakable this is an ad.
        const string bannerText = "ADVERTISEMENT";
        Vector2 bannerTextSize = Game1.smallFont.MeasureString(bannerText);
        int bannerWidth = (int)bannerTextSize.X + 56;
        Rectangle banner = new((viewW - bannerWidth) / 2, top, bannerWidth, bannerHeight);
        b.Draw(Game1.staminaRect, new Rectangle(banner.X - 4, banner.Y - 4, banner.Width + 8, banner.Height + 8), Color.Black * 0.6f * fade);
        b.Draw(Game1.staminaRect, banner, new Color(176, 32, 32) * fade);
        Utility.drawTextWithShadow(b, bannerText, Game1.smallFont,
            new Vector2(banner.X + (banner.Width - bannerTextSize.X) / 2f, banner.Y + (banner.Height - bannerTextSize.Y) / 2f),
            Color.White * fade);

        // 5) Advertiser name, divider, message.
        float y = boxY + pad + bannerHeight / 2;
        DrawCentered(b, heading, headingFont, centerX, y, new Color(140, 30, 20) * fade);
        y += headingFont.LineSpacing + 6;

        b.Draw(Game1.staminaRect, new Rectangle(boxX + pad, (int)y, boxWidth - pad * 2, 4), new Color(120, 70, 40) * (0.5f * fade));
        y += 10;

        foreach (string line in lines)
        {
            DrawCentered(b, line.Trim(), bodyFont, centerX, y, Game1.textColor * fade);
            y += bodyFont.LineSpacing;
        }

        // 6) Footer: time-left bar, hint, countdown (same small font as the menu so the text looks consistent).
        int footerY = boxY + boxHeight - pad - footerHeight + 6;
        Rectangle barBack = new(boxX + pad, footerY, boxWidth - pad * 2, 10);
        float fraction = (float)Math.Clamp(PopupSecondsRemaining / total, 0d, 1d);
        b.Draw(Game1.staminaRect, barBack, new Color(90, 60, 40) * (0.35f * fade));
        b.Draw(Game1.staminaRect, new Rectangle(barBack.X, barBack.Y, (int)(barBack.Width * fraction), barBack.Height), new Color(200, 70, 30) * fade);

        string countdown = $"Closes in {(int)Math.Ceiling(PopupSecondsRemaining)}s";
        float countdownWidth = Game1.smallFont.MeasureString(countdown).X;
        string hint = SubscriptionMenu.TrimToWidth(
            "Tired of ads? Use the AD button.", Game1.smallFont,
            Math.Max(80, textWidth - (int)countdownWidth - 16));

        float textY = footerY + 14;
        Utility.drawTextWithShadow(b, hint, Game1.smallFont, new Vector2(boxX + pad, textY), Game1.textColor * (0.85f * fade));
        Utility.drawTextWithShadow(b, countdown, Game1.smallFont,
            new Vector2(boxX + boxWidth - pad - countdownWidth, textY), Game1.textColor * (0.85f * fade));
    }
}
