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
    private ModConfig Config = new();
    private AdvertisementData Ads = new();
    private readonly Stopwatch AdClock = new();
    private string? CurrentAdvertiser;
    private string? CurrentMessage;
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
                Monitor.Log("advertisements.json was not found; no advertisers can be loaded.", LogLevel.Warn);
                Ads = new AdvertisementData();
                return;
            }

            string json = File.ReadAllText(path);
            Ads = JsonSerializer.Deserialize<AdvertisementData>(json, JsonOptions()) ?? new AdvertisementData();
            Monitor.Log($"Loaded {Ads.Advertisers.Count} advertisers and {Ads.SubscriptionOffers.Count} offers.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Ads = new AdvertisementData();
            Monitor.Log($"Couldn't load advertisements.json: {ex}", LogLevel.Error);
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Subscription = Helper.Data.ReadSaveData<SubscriptionState>("subscription-state") ?? new SubscriptionState();
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
            PopupSecondsRemaining -= Game1.currentGameTime.ElapsedGameTime.TotalSeconds;

        if (IsAdFree())
        {
            AdClock.Restart();
            return;
        }

        double interval = Math.Max(1, Config.AdvertisementIntervalMinutes) * 60d;
        if (AdClock.Elapsed.TotalSeconds >= interval)
        {
            AdClock.Restart();
            if (Config.EnablePopups)
                ShowRandomAdvertisement();
        }
    }

    private bool IsAdFree()
    {
        return (Subscription.RealTimeExpiryUtc is not null && DateTime.UtcNow < Subscription.RealTimeExpiryUtc.Value)
            || (Subscription.InGameDayExpiry is not null && GetAbsoluteGameDay() < Subscription.InGameDayExpiry.Value);
    }

    private static int GetAbsoluteGameDay()
    {
        return Game1.Date.TotalDays;
    }

    private void ShowRandomAdvertisement()
    {
        List<Advertiser> available = Ads.Advertisers
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName))
            .Where(IsAdvertiserUnlocked)
            .Where(p => p.PopupVariations.Count > 0)
            .ToList();

        if (available.Count == 0)
            return;

        Advertiser advertiser = available[Game1.random.Next(available.Count)];
        CurrentAdvertiser = advertiser.DisplayName;
        CurrentMessage = advertiser.PopupVariations[Game1.random.Next(advertiser.PopupVariations.Count)];
        PopupSecondsRemaining = 8;
    }

    private bool IsAdvertiserUnlocked(Advertiser advertiser)
    {
        return advertiser.UnlockCondition.ToLowerInvariant() switch
        {
            "bus" => Game1.player.mailReceived.Contains("ccVault") ||
                     Game1.player.mailReceived.Contains("JojaMember"),
            _ => true
        };
    }

    private static string GetMailKey(Advertiser advertiser, int index)
    {
        return $"BentleysAdvertising_{advertiser.Id}_{index}";
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Mail"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, string> mailData = asset.AsDictionary<string, string>().Data;
            foreach (Advertiser advertiser in Ads.Advertisers)
            {
                for (int i = 0; i < advertiser.MailVariations.Count; i++)
                {
                    MailVariation letter = advertiser.MailVariations[i];
                    string key = GetMailKey(advertiser, i);
                    mailData[key] = $"{letter.Title}%{letter.Body}";
                }
            }
        });
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Config.Enabled || !Config.EnableMail || IsAdFree() || Game1.random.NextDouble() >= Math.Clamp(Config.MailChancePerDay, 0, 1))
            return;

        var candidates = Ads.Advertisers
            .Where(IsAdvertiserUnlocked)
            .SelectMany(advertiser => advertiser.MailVariations.Select((letter, index) => new
            {
                Advertiser = advertiser,
                Letter = letter,
                Index = index,
                Key = GetMailKey(advertiser, index)
            }))
            .Where(item => !Game1.player.mailReceived.Contains(item.Key) && !Game1.player.mailbox.Contains(item.Key))
            .ToList();

        if (candidates.Count == 0)
            return;

        var selected = candidates[Game1.random.Next(candidates.Count)];
        Game1.player.mailbox.Add(selected.Key);
        Monitor.Log($"Queued advertising letter: {selected.Letter.Title}", LogLevel.Trace);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || e.Button != SButton.MouseLeft)
            return;

        Point cursor = e.Cursor.ScreenPixels.ToPoint();

        if (HudButton.Contains(cursor))
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

        int rowHeight = 58;
        int startY = MenuBounds.Y + 76;
        for (int i = 0; i < Ads.SubscriptionOffers.Count; i++)
        {
            Rectangle row = new(MenuBounds.X + 16, startY + i * rowHeight, MenuBounds.Width - 32, 48);
            if (row.Contains(cursor))
            {
                PurchaseOffer(Ads.SubscriptionOffers[i]);
                Helper.Input.Suppress(e.Button);
                return;
            }
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
        else if (offer.DurationInGameDays > 0)
        {
            int currentDay = GetAbsoluteGameDay();
            int startDay = Subscription.InGameDayExpiry is not null && Subscription.InGameDayExpiry.Value > currentDay
                ? Subscription.InGameDayExpiry.Value
                : currentDay;
            Subscription.InGameDayExpiry = startDay + offer.DurationInGameDays;
        }

        CurrentAdvertiser = null;
        CurrentMessage = null;
        PopupSecondsRemaining = 0;
        AdClock.Restart();
        Game1.addHUDMessage(new HUDMessage($"{offer.DisplayName} purchased!", HUDMessage.achievement_type));
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        SpriteBatch batch = e.SpriteBatch;
        int margin = 16;
        HudButton = new Rectangle(Game1.uiViewport.Width - 58 - margin, margin, 42, 42);

        DrawPanel(batch, HudButton, new Color(42, 76, 118, 235));
        batch.DrawString(Game1.smallFont, "AD", new Vector2(HudButton.X + 8, HudButton.Y + 11), Color.White);

        if (PopupSecondsRemaining > 0 && !MenuOpen && CurrentMessage is not null)
        {
            Rectangle popup = new(Game1.uiViewport.Width - 376, 70, 360, 112);
            DrawPanel(batch, popup, new Color(35, 43, 56, 245));
            batch.DrawString(Game1.dialogueFont, CurrentAdvertiser ?? "ADVERTISEMENT",
                new Vector2(popup.X + 14, popup.Y + 10), Color.Gold);
            batch.DrawString(Game1.smallFont, Wrap(CurrentMessage, 43),
                new Vector2(popup.X + 14, popup.Y + 48), Color.White);
        }

        if (MenuOpen)
            DrawSubscriptionMenu(batch);
    }

    private void DrawSubscriptionMenu(SpriteBatch batch)
    {
        int width = 440;
        int height = 100 + Ads.SubscriptionOffers.Count * 58;
        MenuBounds = new Rectangle((Game1.uiViewport.Width - width) / 2,
            (Game1.uiViewport.Height - height) / 2, width, height);

        DrawPanel(batch, MenuBounds, new Color(32, 38, 48, 250));
        batch.DrawString(Game1.dialogueFont, "Advertising Subscriptions",
            new Vector2(MenuBounds.X + 18, MenuBounds.Y + 14), Color.Gold);

        string status = IsAdFree() ? "Advertisements are currently paused." : "Choose an offer to pause advertisements.";
        batch.DrawString(Game1.smallFont, status,
            new Vector2(MenuBounds.X + 18, MenuBounds.Y + 48), Color.White);

        int y = MenuBounds.Y + 76;
        foreach (SubscriptionOffer offer in Ads.SubscriptionOffers)
        {
            Rectangle row = new(MenuBounds.X + 16, y, MenuBounds.Width - 32, 48);
            DrawPanel(batch, row, new Color(67, 77, 91, 245));
            batch.DrawString(Game1.smallFont, $"{offer.DisplayName} - {offer.Price:N0}g",
                new Vector2(row.X + 10, row.Y + 5), Color.White);
            batch.DrawString(Game1.smallFont, offer.Description,
                new Vector2(row.X + 10, row.Y + 25), Color.LightGray);
            y += 58;
        }
    }

    private static void DrawPanel(SpriteBatch batch, Rectangle area, Color color)
    {
        batch.Draw(Game1.staminaRect, area, color);
    }

    private static string Wrap(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;

        List<string> lines = new();
        string line = "";
        foreach (string word in text.Split(' '))
        {
            if ((line + " " + word).Trim().Length > maxChars)
            {
                lines.Add(line);
                line = word;
            }
            else
                line = (line + " " + word).Trim();
        }

        if (line.Length > 0)
            lines.Add(line);

        return string.Join("\n", lines.Take(3));
    }
}