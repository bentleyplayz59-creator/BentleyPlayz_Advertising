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
    private const double PopupDurationSeconds = 9;

    private ModConfig Config = new();
    private AdvertisementData Ads = new();
    private readonly Stopwatch AdClock = new();
    private readonly Dictionary<string, string> DynamicMail = new();
    private string? CurrentAdvertiser;
    private string? CurrentMessage;
    private string? LastAdvertiserId;
    private double PopupSecondsRemaining;
    private bool PopupPending;
    private Rectangle HudButton;
    private SubscriptionState Subscription = new();

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
    ** Save / lifecycle
    *********/
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Subscription = Helper.Data.ReadSaveData<SubscriptionState>("subscription-state") ?? new SubscriptionState();
        ResetPopupState();
        AdClock.Restart();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        ResetPopupState();
        AdClock.Reset();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        Helper.Data.WriteSaveData("subscription-state", Subscription);
    }

    private void ResetPopupState()
    {
        PopupPending = false;
        PopupSecondsRemaining = 0;
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
            AdClock.Restart();
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

        double interval = Math.Max(1, Config.AdvertisementIntervalMinutes) * 60d;
        if (AdClock.Elapsed.TotalSeconds >= interval)
        {
            AdClock.Restart();
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
        PopupSecondsRemaining = PopupDurationSeconds;
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
    ** Input
    *********/
    private bool CanUseHudButton() =>
        Config.EnableSubscriptions && Context.IsWorldReady && Game1.activeClickableMenu == null
        && !Game1.eventUp && Game1.displayHUD;

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!CanUseHudButton() || (e.Button != SButton.MouseLeft && !e.Button.IsUseToolButton()))
            return;

        // UI-scaled coordinates match the rectangles drawn in RenderedHud (raw ScreenPixels do not on Android/zoomed UI).
        Point cursor = e.Cursor.GetScaledScreenPixels().ToPoint();
        if (!HudButton.Contains(cursor))
            return;

        Helper.Input.Suppress(e.Button);
        Game1.playSound("bigSelect");
        Game1.activeClickableMenu = new SubscriptionMenu(Ads.SubscriptionOffers, IsAdFree, PurchaseOffer);
    }

    private bool PurchaseOffer(SubscriptionOffer offer)
    {
        if (!Config.EnableSubscriptions)
            return false;

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

        // Keep each element isolated so one failure can never hide the others.
        SafeDraw(() => DrawHudButton(b), "AD button");
        SafeDraw(() => DrawAdvertisementPopup(b), "advertisement popup");
        SafeDraw(() => DrawDebugTimer(b), "debug timer");
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

    private void DrawHudButton(SpriteBatch b)
    {
        HudButton = new Rectangle(
            Game1.uiViewport.Width - Config.HudButtonOffsetFromRight,
            Config.HudButtonOffsetFromTop,
            48, 48);

        if (!CanUseHudButton())
            return;

        DrawFrame(b, HudButton);
        const string label = "AD";
        Vector2 size = Game1.smallFont.MeasureString(label);
        Utility.drawTextWithShadow(b, label, Game1.smallFont,
            new Vector2(HudButton.X + (HudButton.Width - size.X) / 2f, HudButton.Y + (HudButton.Height - size.Y) / 2f),
            Game1.textColor);
    }

    private static void DrawFrame(SpriteBatch b, Rectangle area) =>
        StardewValley.Menus.IClickableMenu.drawTextureBox(
            b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            area.X, area.Y, area.Width, area.Height, Color.White, 4f, false);

    private void DrawAdvertisementPopup(SpriteBatch b)
    {
        if (PopupSecondsRemaining <= 0 || CurrentMessage is null || !CanShowPopupNow())
            return;

        const int pad = 28;
        int width = Math.Min(380, Game1.uiViewport.Width - 24);
        int textWidth = width - pad * 2;

        string[] lines = Game1.parseText(CurrentMessage, Game1.smallFont, textWidth)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(3)
            .ToArray();

        string heading = CurrentAdvertiser ?? "ADVERTISEMENT";
        SpriteFont headingFont = Game1.dialogueFont.MeasureString(heading).X <= textWidth ? Game1.dialogueFont : Game1.smallFont;
        heading = SubscriptionMenu.TrimToWidth(heading, headingFont, textWidth);

        int headingHeight = headingFont.LineSpacing + 6;
        int height = pad + headingHeight + lines.Length * Game1.smallFont.LineSpacing + pad;

        bool left = Config.PopupCorner.Contains("Left", StringComparison.OrdinalIgnoreCase);
        bool bottom = Config.PopupCorner.Contains("Bottom", StringComparison.OrdinalIgnoreCase);

        int x = left ? 12 : Math.Max(12, HudButton.X - 8 - width);
        int y = bottom ? Game1.uiViewport.Height - height - 110 : 16;

        StardewValley.Menus.IClickableMenu.drawTextureBox(
            b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, height, Color.White);

        Utility.drawTextWithShadow(b, heading, headingFont, new Vector2(x + pad, y + pad - 6), Color.SaddleBrown);

        float textY = y + pad - 6 + headingHeight;
        foreach (string line in lines)
        {
            Utility.drawTextWithShadow(b, line, Game1.smallFont, new Vector2(x + pad, textY), Game1.textColor);
            textY += Game1.smallFont.LineSpacing;
        }
    }

    private static string FormatClock(double seconds)
    {
        // Manual formatting: TimeSpan custom format strings throw FormatException if the colon is mis-escaped.
        int total = (int)Math.Max(0, Math.Round(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }

    private void DrawDebugTimer(SpriteBatch b)
    {
        if (!Config.ShowDebugTimer)
            return;

        double interval = Math.Max(1, Config.AdvertisementIntervalMinutes) * 60d;
        double elapsed = Math.Min(AdClock.Elapsed.TotalSeconds, interval);
        double remaining = Math.Max(0, interval - elapsed);

        string line1 = $"AD DEBUG  {FormatClock(elapsed)} / {FormatClock(interval)}";
        string line2 = IsAdFree()
            ? "Advertisements paused"
            : PopupPending
                ? "Popup waiting for a free moment"
                : $"Next popup in {FormatClock(remaining)}";

        int pad = 10;
        float width = Math.Max(Game1.smallFont.MeasureString(line1).X, Game1.tinyFont.MeasureString(line2).X) + pad * 2;
        int height = Game1.smallFont.LineSpacing + Game1.tinyFont.LineSpacing + pad * 2;

        bool popupTopLeft = Config.PopupCorner.Equals("TopLeft", StringComparison.OrdinalIgnoreCase);
        int y = popupTopLeft && PopupSecondsRemaining > 0 ? 160 : 12;
        Rectangle panel = new(12, y, (int)Math.Ceiling(width), height);

        b.Draw(Game1.staminaRect, panel, new Color(20, 24, 32) * 0.85f);
        Utility.drawTextWithShadow(b, line1, Game1.smallFont, new Vector2(panel.X + pad, panel.Y + pad), Color.Gold);
        Utility.drawTextWithShadow(b, line2, Game1.tinyFont,
            new Vector2(panel.X + pad, panel.Y + pad + Game1.smallFont.LineSpacing), Color.White);
    }
}
