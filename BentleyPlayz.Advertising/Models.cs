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

    /// <summary>Width and height of the AD button in UI pixels (48-128). Bigger is easier to tap on a phone.</summary>
    public int HudButtonSize { get; set; } = 72;

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
    /// <summary>Groups short-duration minute passes separately from standard subscriptions.</summary>
    public string Category { get; set; } = "Standard";
    public int Price { get; set; }

    /// <summary>Ad-free time in minutes of actual play time (the countdown only runs while the game is being played).</summary>
    public int DurationMinutes { get; set; }

    public int DurationInGameDays { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>Everything the mod stores inside a save file (so each save has its own subscription and ad timer).</summary>
public sealed class SubscriptionState
{
    /// <summary>Play-time seconds of ad-free time left from a subscription measured in hours.</summary>
    public double? RealTimeRemainingSeconds { get; set; }

    /// <summary>
    /// Legacy (0.2.x and earlier): wall-clock expiry. Still read so existing saves keep their remaining time;
    /// it is converted to <see cref="RealTimeRemainingSeconds"/> when the save loads and never written again.
    /// </summary>
    public DateTime? RealTimeExpiryUtc { get; set; }

    public int? InGameDayExpiry { get; set; }

    /// <summary>Seconds of play time already counted toward the next ad when the game was last saved.</summary>
    public double? AdTimerSeconds { get; set; }
}

/// <summary>Per-installation settings that are not tied to a save (stored in local-state.json next to the DLL).</summary>
public sealed class LocalState
{
    /// <summary>True once the first-launch welcome screen has been shown.</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>Version of the saved AD button placement; used for one-time position migrations.</summary>
    public int ButtonPositionVersion { get; set; }

    /// <summary>Saved AD button position as a 0-1 fraction of the free horizontal range. Null means the default corner.</summary>
    public float? ButtonX { get; set; }

    /// <summary>Saved AD button position as a 0-1 fraction of the free vertical range. Null means the default corner.</summary>
    public float? ButtonY { get; set; }
}
