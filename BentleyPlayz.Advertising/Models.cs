using StardewModdingAPI;

namespace BentleyPlayz.Advertising;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public int AdvertisementIntervalMinutes { get; set; } = 5;
    public bool EnablePopups { get; set; } = true;
    public bool EnableMail { get; set; } = true;
    public bool EnableSubscriptions { get; set; } = true;

    /// <summary>How long a popup stays on screen, in seconds (clamped to 3-30).</summary>
    public int PopupDurationSeconds { get; set; } = 7;

    /// <summary>How dark the screen gets behind a popup, from 0 (not at all) to 0.9.</summary>
    public float PopupDimAmount { get; set; } = 0.6f;

    /// <summary>Plays a short attention sound when a popup appears.</summary>
    public bool PlayPopupSound { get; set; } = true;

    /// <summary>No longer used: popups are now centered over the game. Kept so existing config files stay valid.</summary>
    public string PopupCorner { get; set; } = "TopRight";

    /// <summary>Shows the on-screen countdown used to verify that popups fire on schedule, and logs taps near the AD button.</summary>
    public bool ShowDebugTimer { get; set; } = true;

    /// <summary>Fallback distance in UI pixels from the right edge of the screen to the left edge of the AD button (used when the clock position can't be read, or AnchorHudButtonToClock is false).</summary>
    public int HudButtonOffsetFromRight { get; set; } = 372;

    /// <summary>Fallback distance in UI pixels from the top of the screen to the top of the AD button.</summary>
    public int HudButtonOffsetFromTop { get; set; } = 20;

    /// <summary>Width and height of the AD button in UI pixels (48-128). Bigger is easier to tap on a phone.</summary>
    public int HudButtonSize { get; set; } = 72;

    /// <summary>Places the AD button just left of the in-game clock instead of using the fixed offsets.</summary>
    public bool AnchorHudButtonToClock { get; set; } = true;

    /// <summary>Keyboard shortcut that opens the subscription menu (set to None to disable).</summary>
    public SButton OpenMenuKey { get; set; } = SButton.K;
}

public sealed class AdvertisementData
{
    public List<Advertiser> Advertisers { get; set; } = new();
    public List<SubscriptionOffer> SubscriptionOffers { get; set; } = new();
}

public sealed class Advertiser
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string UnlockCondition { get; set; } = "Always";
    public List<string> PopupVariations { get; set; } = new();
    public List<MailVariation> MailVariations { get; set; } = new();
}

public sealed class MailVariation
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed class SubscriptionOffer
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Price { get; set; }
    public int DurationMinutes { get; set; }
    public int DurationInGameDays { get; set; }
    public string Description { get; set; } = "";
}

public sealed class SubscriptionState
{
    public DateTime? RealTimeExpiryUtc { get; set; }
    public int? InGameDayExpiry { get; set; }
}
