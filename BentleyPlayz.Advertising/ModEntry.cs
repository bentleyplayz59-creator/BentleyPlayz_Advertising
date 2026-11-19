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
    private const string LocalStateFile = "local-state.json";

    /// <summary>Gap between the AD button and the screen edge, in UI pixels.</summary>
    private const int ButtonMargin = 16;

    /// <summary>Ticks to wait after a save loads before the welcome screen may appear (lets the fade-in finish).</summary>
    private const int WelcomeDelayTicks = 120;

    /// <summary>Active gameplay ticks to wait after loading before the saved ad countdown resumes.</summary>
    private const int AdTimerResumeDelayTicks = 120;

    /// <summary>Consecutive ticks the pointer must read as released; prevents Android touch-state flicker ending a drag.</summary>
    private static readonly int ReleaseGraceTicks = OperatingSystem.IsAndroid() ? 30 : 2;

    private ModConfig Config = new();
    private LocalState Local = new();
    private AdvertisementData Ads = new();
    private readonly Dictionary<string, string> DynamicMail = new();
    private string? CurrentAdvertiser;
    private string? CurrentMessage;
    private string? LastAdvertiserId;
    private double PopupSecondsRemaining;
    private double PopupTotalSeconds;
    private bool PopupPending;
    private bool ForcePopupActive;
    private SubscriptionState Subscription = new();
    private int WelcomeCountdown;
    private bool WelcomeShownThisSave;
    private int AdTimerResumeTicks;

    /// <summary>Play-time seconds counted toward the next ad. Only advances while the game is actively being played.</summary>
    private double AdElapsedSeconds;

    // AD button press / drag state. Track both pointer coordinate sources because Android touch can update them differently.
    private bool Pressing;
    private bool Dragging;
    private Point PressStartScaled;
    private Point PressStartGame;
    private Point PressButtonTopLeft;
    private Point LastPointer;
    private int NotHeldTicks;
    private Point? DragTopLeft;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        LoadLocalState();
        LoadAdvertisements();

        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.Display.RenderedHud += OnRenderedHud;

        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonReleased += OnButtonReleased;
        helper.Events.Content.AssetRequested += OnAssetRequested;

        Monitor.Log($"Bentley's Advertising {ModManifest.Version} loaded.", LogLevel.Info);
    }

    // Public API consumed by Bentley's Advertising Dev Tools.
    public override object? GetApi() => new BentleysAdvertisingApi(this);

    public IReadOnlyList<string> GetAdvertiserIds() =>
        Ads.Advertisers.Where(p => !string.IsNullOrWhiteSpace(p.Id)).Select(p => p.Id).ToList();

    public string GetAdvertiserName(string advertiserId) =>
        FindAdvertiser(advertiserId)?.DisplayName ?? advertiserId;

    public int GetPopupCount(string advertiserId) =>
        FindAdvertiser(advertiserId)?.PopupVariations.Count ?? 0;

    public int GetLetterCount(string advertiserId) =>
        FindAdvertiser(advertiserId)?.MailVariations.Count ?? 0;

    public string? ForcePopup(string advertiserId)
    {
        if (!Context.IsWorldReady)
            return "Load a save before forcing an advertisement.";

        Advertiser? advertiser = FindAdvertiser(advertiserId);
        if (advertiser is null)
            return $"Advertiser '{advertiserId}' was not found.";

        if (advertiser.PopupVariations.Count == 0)
            return $"{advertiser.DisplayName} has no popup variations.";

        CurrentAdvertiser = advertiser.DisplayName;
        CurrentMessage = advertiser.PopupVariations[Game1.random.Next(advertiser.PopupVariations.Count)];
        LastAdvertiserId = advertiser.Id;
        PopupTotalSeconds = Math.Clamp(Config.PopupDurationSeconds, 3, 30);
        PopupSecondsRemaining = PopupTotalSeconds;
        PopupPending = false;
        ForcePopupActive = true;
        if (Config.PlayPopupSound)
            Game1.playSound("dwop");

        Monitor.Log($"Dev Tools forced a popup from {advertiser.DisplayName}.", LogLevel.Trace);
        return null;
    }

    public string? ForceLetter(string advertiserId, bool deliverNow)
    {
        if (!Context.IsWorldReady)
            return "Load a save before forcing an advertiser letter.";

        Advertiser? advertiser = FindAdvertiser(advertiserId);
        if (advertiser is null)
            return $"Advertiser '{advertiserId}' was not found.";

        if (advertiser.MailVariations.Count == 0)
            return $"{advertiser.DisplayName} has no letter variations.";

        List<int> available = Enumerable.Range(0, advertiser.MailVariations.Count)
            .Where(index =>
            {
                string mailId = GetMailId(advertiser.Id, index);
                return !Game1.player.mailReceived.Contains(mailId)
                    && !Game1.player.mailbox.Contains(mailId)
                    && !Game1.player.mailForTomorrow.Contains(mailId);
            })
            .ToList();

        if (available.Count == 0)
            return $"All letters from {advertiser.DisplayName} have already been received or queued.";

        int variation = available[Game1.random.Next(available.Count)];
        string selectedMailId = GetMailId(advertiser.Id, variation);
        if (deliverNow)
            Game1.player.mailbox.Add(selectedMailId);
        else
            Game1.player.mailForTomorrow.Add(selectedMailId);

        Monitor.Log($"Dev Tools queued a letter from {advertiser.DisplayName} ({(deliverNow ? "mailbox now" : "tomorrow")}).", LogLevel.Trace);
        return null;
    }

    public void ClearAdFree()
    {
        Subscription.RealTimeRemainingSeconds = null;
        Subscription.InGameDayExpiry = null;
        Monitor.Log("Dev Tools cleared the active ad-free subscription.", LogLevel.Trace);
    }

    public string GetStatusText()
    {
        if (!Context.IsWorldReady)
            return "Load a save to see advertising status.";

        return IsAdFree() ? $"Ad-free: {GetRemainingText()}" : GetNextAdText();
    }

    private Advertiser? FindAdvertiser(string advertiserId) =>
        Ads.Advertisers.FirstOrDefault(p => string.Equals(p.Id, advertiserId, StringComparison.OrdinalIgnoreCase));

    /*********
    ** Data loading
    *********/
    private void LoadLocalState()
    {
        try
        {
            Local = Helper.Data.ReadJsonFile<LocalState>(LocalStateFile) ?? new LocalState();
        }
        catch (Exception ex)
        {
            Local = new LocalState();
            Monitor.Log($"Couldn't read {LocalStateFile}; using defaults. {ex.Message}", LogLevel.Warn);
        }

        // Undo the previous forced bottom-left migration. Its marker and exact coordinates identify the
        // position written by that migration, so restore the original bottom-right default once.
        if (Local.ButtonPositionVersion == 2 && Local.ButtonX == 0f && Local.ButtonY == 1f)
        {
            Local.ButtonX = null;
            Local.ButtonY = null;
        }

        if (Local.ButtonPositionVersion < 3)
        {
            Local.ButtonPositionVersion = 3;
            SaveLocalState();
        }
    }

    private void SaveLocalState()
    {
        try
        {
            Helper.Data.WriteJsonFile(LocalStateFile, Local);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Couldn't save {LocalStateFile}: {ex.Message}", LogLevel.Warn);
        }
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

        // Keep the sender attribution as the final line of every generated in-game letter.
        // Reserve several lines above the attribution so it sits near the bottom of the vanilla letter panel.
        const string signatureGap = "\n\n\n\n\n";
        body = body.Length > 0
            ? body + signatureGap + "Sent from Bentley's Advertising."
            : "Sent from Bentley's Advertising.";

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
    ** Ad timer (per save, play time only)
    *********/
    private double GetIntervalSeconds() =>
        Math.Max(1, Config.AdvertisementIntervalMinutes) * 60d;

    /// <summary>Starts a fresh countdown to the next ad.</summary>
    private void ResetAdClock() => AdElapsedSeconds = 0;

    /// <summary>
    /// True only during active world gameplay. SMAPI's IsPlayerFree excludes menus, dialogue, and events;
    /// the explicit game-state checks cover sleeping and day/fade transitions too.
    /// </summary>
    private static bool IsActivelyPlaying() =>
        Context.IsWorldReady
        && Game1.game1.IsActive
        && Context.IsPlayerFree
        && Game1.activeClickableMenu is null
        && !Game1.dialogueUp
        && !Game1.eventUp
        && !Game1.newDay
        && !Game1.fadeToBlack
        && Game1.fadeToBlackAlpha <= 0f
        && Game1.player is not null
        && !Game1.player.isInBed.Value;

    /// <summary>Elapsed seconds for this update, capped to prevent lag spikes from advancing the timer.</summary>
    private static double GetTickSeconds()
    {
        double elapsed = Game1.currentGameTime?.ElapsedGameTime.TotalSeconds ?? 0d;
        return double.IsFinite(elapsed) ? Math.Clamp(elapsed, 0d, 0.25d) : 0d;
    }

    /*********
    ** Save / lifecycle
    *********/
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // Everything here is stored inside the save file, so each save has its own subscription and ad timer.
        Subscription = Helper.Data.ReadSaveData<SubscriptionState>("subscription-state") ?? new SubscriptionState();
        WelcomeShownThisSave = string.Equals(Helper.Data.ReadSaveData<string>("welcome-shown"), "shown", StringComparison.Ordinal);
        MigrateLegacySubscription();
        ResetPopupState();
        CancelButtonPress();

        // Restore the saved progress without resetting it. The first two seconds of eligible gameplay after
        // loading are a resume grace period, so loading itself can never trigger an ad.
        double interval = GetIntervalSeconds();
        double saved = Subscription.AdTimerSeconds ?? 0;
        AdElapsedSeconds = Math.Clamp(saved, 0, interval);
        AdTimerResumeTicks = AdTimerResumeDelayTicks;

        WelcomeCountdown = WelcomeDelayTicks;
    }

    /// <summary>
    /// Saves from 0.2.x stored a wall-clock expiry time. Convert whatever was left into play-time seconds so existing
    /// subscriptions carry over, and stop using the wall-clock field.
    /// </summary>
    private void MigrateLegacySubscription()
    {
        if (Subscription.RealTimeExpiryUtc is not DateTime legacy)
            return;

        DateTime expiry = legacy.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(legacy, DateTimeKind.Utc)
            : legacy.ToUniversalTime();
        double left = (expiry - DateTime.UtcNow).TotalSeconds;
        if (left > 0)
            Subscription.RealTimeRemainingSeconds = Math.Max(Subscription.RealTimeRemainingSeconds ?? 0d, left);

        Subscription.RealTimeExpiryUtc = null;
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        // Nothing counts down on the title screen; the next loaded save sets its own timer.
        ResetPopupState();
        CancelButtonPress();
        AdElapsedSeconds = 0;
        AdTimerResumeTicks = 0;
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {        // Remember how far into the countdown we are, so quitting and loading this save resumes from here.
        Subscription.AdTimerSeconds = IsAdFree() ? 0 : AdElapsedSeconds;
        Helper.Data.WriteSaveData("subscription-state", Subscription);
    }

    private void ResetPopupState()
    {
        PopupPending = false;
        ForcePopupActive = false;
        PopupSecondsRemaining = 0;
        PopupTotalSeconds = 0;
        CurrentAdvertiser = null;
        CurrentMessage = null;
        LastAdvertiserId = null;
    }

    /*********
    ** Per-tick update
    *********/
    /// <summary>
    /// Popups are drawn on the HUD layer, which is hidden during menus and cutscenes. An ad that fires then
    /// waits until the player can actually see it.
    /// </summary>
    private static bool CanShowPopupNow() =>
        Context.IsWorldReady && Game1.activeClickableMenu == null && !Game1.eventUp && Game1.displayHUD;

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        UpdateButtonPress();

        if (!Context.IsWorldReady || !Config.Enabled)
            return;

        ShowWelcomeIfNeeded();

        bool playing = IsActivelyPlaying();
        double delta = playing ? GetTickSeconds() : 0d;
        double adDelta = delta;
        if (playing && AdTimerResumeTicks > 0)
        {
            AdTimerResumeTicks--;
            adDelta = 0d;
        }

        // Subscription time left only counts down while the game is actually being played.
        if (delta > 0 && Subscription.RealTimeRemainingSeconds is double remaining)
        {
            remaining -= delta;
            Subscription.RealTimeRemainingSeconds = remaining > 0 ? remaining : null;
        }

        if (Subscription.InGameDayExpiry is not null && GetAbsoluteGameDay() >= Subscription.InGameDayExpiry.Value)
            Subscription.InGameDayExpiry = null;

        if (IsAdFree())
        {
            ResetAdClock();
            PopupPending = false;

            if (ForcePopupActive)
            {
                if (PopupSecondsRemaining > 0 && CanShowPopupNow() && delta > 0)
                    PopupSecondsRemaining = Math.Max(0, PopupSecondsRemaining - delta);

                if (PopupSecondsRemaining <= 0)
                {
                    ForcePopupActive = false;
                    CurrentAdvertiser = null;
                    CurrentMessage = null;
                }
            }
            else
            {
                PopupSecondsRemaining = 0;
            }

            return;
        }

        bool canShow = CanShowPopupNow();

        // Only count the popup's lifetime down while it can actually be seen.
        if (PopupSecondsRemaining > 0 && canShow && delta > 0)
        {
            PopupSecondsRemaining = Math.Max(0, PopupSecondsRemaining - delta);
            if (PopupSecondsRemaining <= 0)
            {
                ForcePopupActive = false;
                CurrentAdvertiser = null;
                CurrentMessage = null;
            }
        }

        AdElapsedSeconds += adDelta;
        if (AdElapsedSeconds >= GetIntervalSeconds())
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
        (Subscription.RealTimeRemainingSeconds is double seconds && seconds > 0) ||
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
        Monitor.Log($"Showing popup from {advertiser.DisplayName}.", LogLevel.Trace);
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
    ** Welcome screen (once per installation)
    *********/
    private void ShowWelcomeIfNeeded()
    {
        if (WelcomeShownThisSave)
            return;

        if (WelcomeCountdown > 0)
        {
            WelcomeCountdown--;
            return;
        }

        // Wait for a quiet moment: no menu, cutscene, festival or dialogue on screen.
        if (!Context.CanPlayerMove || Game1.activeClickableMenu != null)
            return;

        // Record it in this save before opening, so it appears once per save rather than once per installation.
        WelcomeShownThisSave = true;
        Helper.Data.WriteSaveData("welcome-shown", "shown");

        string? keyName = Config.OpenMenuKey == SButton.None ? null : Config.OpenMenuKey.ToString();
        Game1.activeClickableMenu = new WelcomeMenu(keyName);
    }

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

        if (Subscription.RealTimeRemainingSeconds is double seconds && seconds > 0)
            parts.Add($"{FormatRemaining(TimeSpan.FromSeconds(seconds))} left");

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

        double remaining = Math.Max(0, GetIntervalSeconds() - AdElapsedSeconds);
        return $"Next ad in {FormatRemaining(TimeSpan.FromSeconds(Math.Ceiling(remaining)))}";
    }

    /*********
    ** AD button: position, input and dragging
    *********/
    private bool CanUseHudButton() =>
        Config.Enabled && Config.EnableSubscriptions && Context.IsWorldReady && Game1.activeClickableMenu == null
        && !Game1.eventUp && Game1.displayHUD;

    private int GetButtonSize() => Math.Clamp(Config.HudButtonSize, 48, 128);

    /// <summary>The range the button's top-left corner may occupy so it always stays fully on screen.</summary>
    private static void GetButtonRange(int size, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = ButtonMargin;
        minY = ButtonMargin;
        maxX = Math.Max(minX, Game1.uiViewport.Width - size - ButtonMargin);
        maxY = Math.Max(minY, Game1.uiViewport.Height - size - ButtonMargin);
    }

    /// <summary>
    /// One rectangle is used for both drawing and clicking, so the two can't drift apart.
    /// Default is the original bottom-right corner; a saved or in-progress drag position overrides it. The button remains draggable and is clamped on screen.
    /// </summary>
    private Rectangle GetHudButtonBounds()
    {
        int size = GetButtonSize();
        GetButtonRange(size, out int minX, out int minY, out int maxX, out int maxY);

        int x;
        int y;
        if (DragTopLeft is Point drag)
        {
            x = drag.X;
            y = drag.Y;
        }
        else if (Local.ButtonX is float fx && Local.ButtonY is float fy)
        {
            x = minX + (int)Math.Round(Math.Clamp(fx, 0f, 1f) * (maxX - minX));
            y = minY + (int)Math.Round(Math.Clamp(fy, 0f, 1f) * (maxY - minY));
        }
        else
        {
            x = maxX;
            y = maxY;
        }

        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);
        return new Rectangle(x, y, size, size);
    }

    /// <summary>The tappable area: the drawn button plus a little slack, because fingers are bigger than cursors.</summary>
    private Rectangle GetHudButtonHitArea()
    {
        Rectangle hit = GetHudButtonBounds();
        hit.Inflate(10, 10);
        return hit;
    }

    private void StoreButtonPosition(Rectangle bounds)
    {
        GetButtonRange(bounds.Width, out int minX, out int minY, out int maxX, out int maxY);
        Local.ButtonX = maxX > minX ? (float)(bounds.X - minX) / (maxX - minX) : 0f;
        Local.ButtonY = maxY > minY ? (float)(bounds.Y - minY) / (maxY - minY) : 0f;
    }

    /// <summary>Puts the AD button back in its original default bottom-right corner.</summary>
    private void ResetButtonPosition()
    {
        Local.ButtonX = null;
        Local.ButtonY = null;
        DragTopLeft = null;
        SaveLocalState();
    }

    private void OpenSubscriptionMenu()
    {
        Game1.playSound("bigSelect");
        Game1.activeClickableMenu = new SubscriptionMenu(Ads.SubscriptionOffers, IsAdFree, GetRemainingText, GetNextAdText, PurchaseOffer, ResetButtonPosition);
    }

    /// <summary>The pointer in SMAPI's UI-scaled screen pixels.</summary>
    private Point GetScaledPointer() =>
        Helper.Input.GetCursorPosition().GetScaledScreenPixels().ToPoint();

    /// <summary>The pointer in the same space vanilla menus use for their own clicks.</summary>
    private static Point GetGamePointer() =>
        new(Game1.getMouseX(true), Game1.getMouseY(true));

    /// <summary>
    /// Whether a finger/mouse button is still down. On Android a touch is reported as a left mouse press by the game
    /// but SMAPI's own input state can read as released while the finger is held, so both are checked.
    /// </summary>
    private bool IsPointerHeld() =>
        Helper.Input.IsDown(SButton.MouseLeft)
        || Game1.input.GetMouseState().LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;

    private void CancelButtonPress()
    {
        Pressing = false;
        Dragging = false;
        NotHeldTicks = 0;
        DragTopLeft = null;
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

        // Mouse or touch: a quick tap opens the menu on release, press-and-drag moves the button.
        if (e.Button == SButton.MouseLeft)
        {
            BeginPress(e);
            return;
        }

        // Gamepad use-tool button while the cursor is over the AD button.
        if (!e.Button.IsUseToolButton())
            return;

        Point scaled = e.Cursor.GetScaledScreenPixels().ToPoint();
        Point game = GetGamePointer();
        Rectangle hit = GetHudButtonHitArea();
        if (!hit.Contains(scaled) && !hit.Contains(game))
            return;

        Helper.Input.Suppress(e.Button);        OpenSubscriptionMenu();
    }

    private void BeginPress(ButtonPressedEventArgs e)
    {
        // The button is drawn in UI space, so compare against UI-scaled coordinates. Game1.getMouseX/Y(true) is the
        // same space vanilla menus use for their own clicks; accepting either covers any difference between them.
        Rectangle hit = GetHudButtonHitArea();
        Point scaled = e.Cursor.GetScaledScreenPixels().ToPoint();
        Point game = GetGamePointer();
        if (!hit.Contains(scaled) && !hit.Contains(game))
            return;

        Rectangle bounds = GetHudButtonBounds();
        Pressing = true;
        Dragging = false;
        NotHeldTicks = 0;
        PressStartScaled = scaled;
        PressStartGame = game;
        PressButtonTopLeft = new Point(bounds.X, bounds.Y);
        LastPointer = hit.Contains(scaled) ? scaled : game;
        Helper.Input.Suppress(e.Button);
    }

    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (e.Button == SButton.MouseLeft && Pressing)
        {
            // Android can emit a short-lived synthetic mouse release during a finger gesture.
            // Defer tap completion so the following touch-position updates can still become a drag.
            NotHeldTicks = Math.Max(NotHeldTicks, 1);
        }
    }

    /// <summary>
    /// How far the pointer has moved since the press began. Both coordinate sources are tracked and whichever has
    /// moved further wins, so dragging works whether the platform keeps updating SMAPI's cursor or only the game's.
    /// </summary>
    private Point GetPointerDelta()
    {
        Point scaled = GetScaledPointer();
        Point game = GetGamePointer();
        Point dScaled = new(scaled.X - PressStartScaled.X, scaled.Y - PressStartScaled.Y);
        Point dGame = new(game.X - PressStartGame.X, game.Y - PressStartGame.Y);

        long lenScaled = (long)dScaled.X * dScaled.X + (long)dScaled.Y * dScaled.Y;
        long lenGame = (long)dGame.X * dGame.X + (long)dGame.Y * dGame.Y;
        return lenScaled >= lenGame ? dScaled : dGame;
    }

    /// <summary>Follows the finger/cursor while a press is held, turning it into a drag once it moves far enough.</summary>
    private void UpdateButtonPress()
    {
        if (!Pressing)
            return;

        if (!CanUseHudButton())
        {
            CancelButtonPress();
            return;
        }

        // Keep the game from treating the held press as a tool swing or walk command.
        Helper.Input.Suppress(SButton.MouseLeft);

        // Process movement before release timing. Android may briefly report a synthetic mouse-up while
        // the finger is still moving, so the grace window must not finish the gesture before drag detection.
        Point delta = GetPointerDelta();
        if (!Dragging)
        {
            int slop = Math.Max(12, GetButtonSize() / 5);
            if (delta.X * delta.X + delta.Y * delta.Y >= slop * slop)
                Dragging = true;
        }

        if (Dragging)
            DragTopLeft = new Point(PressButtonTopLeft.X + delta.X, PressButtonTopLeft.Y + delta.Y);

        if (!IsPointerHeld())
        {
            if (++NotHeldTicks >= ReleaseGraceTicks)
            {
                FinishPress();
                return;
            }
        }
        else
        {
            NotHeldTicks = 0;
        }
    }

    private void FinishPress()
    {
        if (!Pressing)
            return;

        bool wasDragging = Dragging;
        Pressing = false;
        Dragging = false;
        NotHeldTicks = 0;

        if (wasDragging)
        {
            // A drag never opens the menu; it just saves the new position.
            Rectangle bounds = GetHudButtonBounds();
            DragTopLeft = null;
            StoreButtonPosition(bounds);
            SaveLocalState();
            return;
        }

        DragTopLeft = null;
        if (!CanUseHudButton())
            return;

        // A tap: open the menu if the finger is still on the button (checked in both coordinate spaces).
        Rectangle hit = GetHudButtonHitArea();
        if (hit.Contains(GetScaledPointer()) || hit.Contains(GetGamePointer()) || hit.Contains(LastPointer))
            OpenSubscriptionMenu();
    }

    /*********
    ** Purchasing
    *********/
    private bool PurchaseOffer(SubscriptionOffer offer)
    {
        if (!Config.EnableSubscriptions)
            return false;

        if (offer.DurationMinutes <= 0 && offer.DurationInGameDays <= 0)
        {
            Monitor.Log($"Offer '{offer.Id}' has no duration, so it can't be purchased.", LogLevel.Warn);
            return false;
        }

        if (offer.Price < 0)
        {
            Monitor.Log($"Offer '{offer.Id}' has a negative price, so it can't be purchased.", LogLevel.Warn);
            return false;
        }

        if (Game1.player.Money < offer.Price)
        {
            Game1.addHUDMessage(new HUDMessage("You don't have enough gold.", HUDMessage.error_type));
            return false;
        }

        Game1.player.Money -= offer.Price;

        if (offer.DurationMinutes > 0)
            Subscription.RealTimeRemainingSeconds = Math.Max(0d, Subscription.RealTimeRemainingSeconds ?? 0d) + offer.DurationMinutes * 60d;

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

    private void DrawHudButton(SpriteBatch b)
    {
        if (!CanUseHudButton())
            return;

        Rectangle area = GetHudButtonBounds();
        bool adFree = IsAdFree();
        bool hovered = GetHudButtonHitArea().Contains(Game1.getMouseX(true), Game1.getMouseY(true));

        // Drop shadow so the button doesn't melt into the HUD (a little deeper while it is being dragged).
        int lift = Dragging ? 8 : 4;
        b.Draw(Game1.staminaRect, new Rectangle(area.X + lift, area.Y + lift + 2, area.Width, area.Height), Color.Black * 0.35f);

        Color tint = Dragging ? Color.Orange : adFree ? Color.LightGreen : (hovered || Pressing) ? Color.Yellow : Color.Gold;
        StardewValley.Menus.IClickableMenu.drawTextureBox(
            b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            area.X, area.Y, area.Width, area.Height, tint, 4f, false);

        const string label = "AD";
        SpriteFont labelFont = Game1.dialogueFont;
        Vector2 size = labelFont.MeasureString(label);
        if (size.X > area.Width - 14)
        {
            labelFont = Game1.smallFont;
            size = labelFont.MeasureString(label);
        }

        Utility.drawTextWithShadow(b, label, labelFont,
            new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f),
            adFree ? Color.DarkGreen : new Color(140, 30, 20));
    }

    private static void DrawCentered(SpriteBatch b, string text, SpriteFont font, float centerX, float y, Color color)
    {
        Vector2 size = font.MeasureString(text);        Utility.drawTextWithShadow(b, text, font, new Vector2(centerX - size.X / 2f, y), color);
    }

    /// <summary>
    /// A deliberately loud interruption: the whole view dims and a big parchment box with a red ADVERTISEMENT tab
    /// appears in the middle of the screen. Movement is NOT blocked - the player keeps control throughout.
    /// </summary>
    private void DrawAdvertisementPopup(SpriteBatch b)
    {
        if (PopupSecondsRemaining <= 0 || CurrentMessage is null || (IsAdFree() && !ForcePopupActive) || !CanShowPopupNow())
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