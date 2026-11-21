namespace BentleyPlayz.Advertising;

/// <summary>Client-to-host request. The host validates the advertiser and connected recipient before forwarding.</summary>
internal sealed class AdvertisementPopupRequest
{
    public string AdvertiserId { get; set; } = "";
    public long TargetPlayerId { get; set; }
    public string RequestingModId { get; set; } = "";
}

/// <summary>Host-to-recipient delivery. Recipients only accept this message from the farm host.</summary>
internal sealed class AdvertisementPopupDelivery
{
    public string AdvertiserId { get; set; } = "";
}
