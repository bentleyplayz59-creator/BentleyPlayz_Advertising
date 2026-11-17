namespace BentleyPlayz.Advertising;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public int AdvertisementIntervalMinutes { get; set; } = 5;
    public bool EnablePopups { get; set; } = true;
    public bool EnableMail { get; set; } = true;
    public bool EnableSubscriptions { get; set; } = true;
    public string PopupCorner { get; set; } = "TopRight";
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
