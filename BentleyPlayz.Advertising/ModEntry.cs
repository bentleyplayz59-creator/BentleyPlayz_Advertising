using System.Diagnostics;
using System.Text.Json;
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
    private string? CurrentAdvertiserId;
    private string? CurrentAdvertiser;
    private string? CurrentMessage;
    private string? LastAdvertiserId;
    private double PopupSecondsRemaining;
    private bool MenuOpen;
    private Rectangle HudButton;
    private Rectangle MenuBounds;
    private SubscriptionState Subscription = new();

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        LoadAdvertisements();

        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.Display.RenderedHud += OnRenderedHud;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Content.AssetRequested += OnAssetRequested;

        AdClock.Start();
        Monitor.Log("Bentley's Advertising initialized.", LogLevel.Info);
    }

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
                    DynamicMail[GetMailId(advertiser.Id, i)] = $"{mail.Title}^ {mail.Body.Replace("\\n", "\n")}";
                }
            }

            Monitor.Log($"Loaded {Ads.Advertisers.Count} advertisers and {Ads.SubscriptionOffers.Count} subscription offers.", LogLevel.Info);
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

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                foreach (KeyValuePair<string, string> mail in DynamicMail)
                    data[mail.Key] = mail.Value;
            });
        }
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Subscription = Helper.Data.ReadSaveData<SubscriptionState>("subscription-state") ?? new SubscriptionState();
        AdClock.Restart();
        MenuOpen = false;
        PopupSecondsRemaining = 0;
        LastAdvertiserId = null;
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        Helper.Data.WriteSaveData("subscription-state", Subscription);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled)
            return;

        if (Subscription.RealTimeExpiryUtc is not null && DateTime.UtcNow >= Subscription.RealTimeExpiryUtc.Value)
            Subscription.RealTimeExpiryUtc = null;

        if (Subscription.InGameDayExpiry is not null && GetAbsoluteGameDay() >= Subscription.InGameDayExpiry.Value)
            Subscription.InGameDayExpiry = null;

        if (PopupSecondsRemaining > 0)
            PopupSecondsRemaining = Math.Max(0, PopupSecondsRemaining - Game1.currentGameTime.ElapsedGameTime.TotalSeconds);

        if (IsAdFree())
            return;

        double interval = Math.Max(1, Config.AdvertisementIntervalMinutes) * 60d;
        if (AdClock.Elapsed.TotalSeconds >= interval)
        {
            AdClock.Restart();
            if (Config.EnablePopups)
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
            .Where(IsAdvertiserUnlocked)
            .Where(p => p.PopupVariations.Count > 0)
            .ToList();

        if (available.Count == 0)
            return;

        List<Advertiser> alternatives = available.Where(p => p.Id != LastAdvertiserId).ToList();
        if (alternatives.Count > 0)
            available = alternatives;

        Advertiser advertiser = available[Game1.random.Next(available.Count)];
        CurrentAdvertiserId = advertiser.Id;
        CurrentAdvertiser = advertiser.DisplayName;
        CurrentMessage = advertiser.PopupVariations[Game1.random.Next(advertiser.PopupVariations.Count)];
        LastAdvertiserId = advertiser.Id;
        PopupSecondsRemaining = 9;
    }

    private bool IsAdvertiserUnlocked(Advertiser advertiser) =>
        advertiser.UnlockCondition.ToLowerInvariant() switch
        {
            "bus" => Game1.MasterPlayer.mailReceived.Contains("ccVault")
                || Game1.MasterPlayer.mailReceived.Contains("jojaVault"),
            "communitycenter" => Game1.MasterPlayer.mailReceived.Contains("ccIsComplete"),
            "mines" or "mine" or "guild" => Game1.MasterPlayer.mailReceived.Contains("guildQuest"),
            _ => true
        };

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
        string id = GetMailId(selected.Id, variation);
        Game1.player.mailForTomorrow.Add(id);
        Helper.GameContent.InvalidateCache("Data/mail");
        Monitor.Log($"Queued an advertising letter from {selected.DisplayName}.", LogLevel.Trace);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || e.Button != SButton.MouseLeft)
            return;

        Point cursor = e.Cursor.ScreenPixels.ToPoint();
        if (Config.EnableSubscriptions && HudButton.Contains(cursor))
        {
            MenuOpen = !MenuOpen;
            Helper.Input.Suppress(e.Button);
            return;
        }

        if (!MenuOpen)
            return;

        if (!MenuBounds.Contains(cursor))
        {
            MenuOpen = false;
            return;
        }

        int y = MenuBounds.Y + 82;
        foreach (SubscriptionOffer offer in Ads.SubscriptionOffers)
        {
            Rectangle row = new(MenuBounds.X + 16, y, MenuBounds.Width - 32, 72);
            if (row.Contains(cursor))
            {
                PurchaseOffer(offer);
                Helper.Input.Suppress(e.Button);
                return;
            }
            y += 80;
        }
    }

    private void PurchaseOffer(SubscriptionOffer offer)
    {
        if (!Config.EnableSubscriptions)
            return;

        if (Game1.player.Money < offer.Price)
        {
            Game1.addHUDMessage(new HUDMessage("You don't have enough gold.", HUDMessage.error_type));
            return;
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

        Game1.addHUDMessage(new HUDMessage($"{offer.DisplayName} purchased!", HUDMessage.achievement_type));
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled)
            return;

        SpriteBatch b = e.SpriteBatch;
        HudButton = new Rectangle(Game1.uiViewport.Width - 58, 16, 42, 42);

        if (Config.EnableSubscriptions)
        {
            DrawPanel(b, HudButton, new Color(42, 76, 118, 235));
            b.DrawString(Game1.smallFont, "AD", new Vector2(HudButton.X + 8, HudButton.Y + 11), Color.White);
        }

        if (PopupSecondsRemaining > 0 && !MenuOpen && CurrentMessage is not null)
            DrawAdvertisementPopup(b);

        if (MenuOpen && Config.EnableSubscriptions)
            DrawSubscriptionMenu(b);

        DrawDebugTimer(b);
    }

    private void DrawAdvertisementPopup(SpriteBatch b)
    {
        int width = Math.Min(360, Game1.uiViewport.Width - 24);
        int height = 118;
        int x = Config.PopupCorner.Equals("TopLeft", StringComparison.OrdinalIgnoreCase)
            ? 12
            : Game1.uiViewport.Width - width - 12;
        int y = Config.PopupCorner.Equals("BottomRight", StringComparison.OrdinalIgnoreCase)
            || Config.PopupCorner.Equals("BottomLeft", StringComparison.OrdinalIgnoreCase)
            ? Game1.uiViewport.Height - height - 18
            : 70;

        Rectangle popup = new(x, y, width, height);
        DrawPanel(b, popup, new Color(35, 43, 56, 245));
        b.DrawString(Game1.dialogueFont, CurrentAdvertiser ?? "ADVERTISEMENT",
            new Vector2(popup.X + 14, popup.Y + 10), Color.Gold);
        DrawWrappedText(b, CurrentMessage ?? "", Game1.smallFont,
            new Vector2(popup.X + 14, popup.Y + 48), Color.White, width - 28, 3);
    }

    private static void DrawWrappedText(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color, float maxWidth, int maxLines)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> lines = new();
        string line = "";

        foreach (string word in words)
        {
            string candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (font.MeasureString(candidate).X > maxWidth && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
            lines.Add(line);

        for (int i = 0; i < Math.Min(lines.Count, maxLines); i++)
            b.DrawString(font, lines[i], position + new Vector2(0, i * font.LineSpacing), color);
    }

    private void DrawSubscriptionMenu(SpriteBatch b)
    {
        int width = Math.Min(440, Game1.uiViewport.Width - 24);
        int rowHeight = 72;
        int rowSpacing = 8;
        int height = 112 + Ads.SubscriptionOffers.Count * (rowHeight + rowSpacing);
        MenuBounds = new Rectangle((Game1.uiViewport.Width - width) / 2,
            (Game1.uiViewport.Height - height) / 2, width, height);

        DrawPanel(b, MenuBounds, new Color(32, 38, 48, 250));
        b.DrawString(Game1.dialogueFont, "Advertising Subscriptions",
            new Vector2(MenuBounds.X + 18, MenuBounds.Y + 14), Color.Gold);
        b.DrawString(Game1.smallFont,
            IsAdFree() ? "Advertisements are currently paused." : "Choose an offer to pause advertisements.",
            new Vector2(MenuBounds.X + 18, MenuBounds.Y + 48), Color.White);

        int y = MenuBounds.Y + 82;
        foreach (SubscriptionOffer offer in Ads.SubscriptionOffers)
        {
            Rectangle row = new(MenuBounds.X + 16, y, MenuBounds.Width - 32, rowHeight);
            DrawPanel(b, row, new Color(67, 77, 91, 245));
            b.DrawString(Game1.smallFont, $"{offer.DisplayName} - {offer.Price:N0}g",
                new Vector2(row.X + 10, row.Y + 4), Color.White);
            DrawWrappedText(b, offer.Description, Game1.tinyFont,
                new Vector2(row.X + 10, row.Y + 31), Color.LightGray, row.Width - 20, 2);
            y += rowHeight + rowSpacing;
        }
    }

    private void DrawDebugTimer(SpriteBatch b)
    {
        double interval = Math.Max(1, Config.AdvertisementIntervalMinutes) * 60d;
        double elapsed = AdClock.Elapsed.TotalSeconds;
        double remaining = Math.Max(0, interval - (elapsed % interval));
        bool paused = IsAdFree();
        string elapsedText = TimeSpan.FromSeconds(elapsed % interval).ToString(@"mm\\:ss");
        string remainingText = TimeSpan.FromSeconds(remaining).ToString(@"mm\\:ss");
        string intervalText = TimeSpan.FromSeconds(interval).ToString(@"mm\\:ss");
        string line1 = $"AD DEBUG  {elapsedText} / {intervalText}";
        string line2 = paused ? "Advertisements paused" : $"Next popup in {remainingText}";
        int width = 238;
        int height = 58;
        Rectangle panel = new((Game1.uiViewport.Width - width) / 2, 12, width, height);
        DrawPanel(b, panel, new Color(25, 30, 38, 220));
        b.DrawString(Game1.smallFont, line1, new Vector2(panel.X + 9, panel.Y + 5), Color.Gold);
        b.DrawString(Game1.tinyFont, line2, new Vector2(panel.X + 9, panel.Y + 34), Color.White);
    }

    private static void DrawPanel(SpriteBatch b, Rectangle area, Color color) =>
        b.Draw(Game1.staminaRect, area, color);
}