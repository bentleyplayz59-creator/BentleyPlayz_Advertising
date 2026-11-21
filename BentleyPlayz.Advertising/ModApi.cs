namespace BentleyPlayz.Advertising;

/// <summary>
/// Public API other SMAPI mods can request through
/// <c>Helper.ModRegistry.GetApi&lt;IBentleysAdvertisingApi&gt;("BentleyPlayz.Advertising")</c>.
/// Companion mods should copy this interface and the integration info type into their own project.
/// </summary>
public interface IBentleysAdvertisingApi
{
    /// <summary>Gets the IDs of every advertiser in advertisements.json.</summary>
    IReadOnlyList<string> GetAdvertiserIds();

    /// <summary>Gets an advertiser's display name, or its ID if unknown.</summary>
    string GetAdvertiserName(string advertiserId);

    /// <summary>Gets the number of popup variations for an advertiser.</summary>
    int GetPopupCount(string advertiserId);

    /// <summary>Gets the number of letter variations for an advertiser.</summary>
    int GetLetterCount(string advertiserId);

    /// <summary>Shows an advertisement popup locally as soon as no menu is open. Works even while ad-free.</summary>
    string? ForcePopup(string advertiserId);

    /// <summary>Delivers an advertiser's letter now or tomorrow.</summary>
    string? ForceLetter(string advertiserId, bool deliverNow);

    /// <summary>Removes active ad-free subscription time in the current player's save data.</summary>
    void ClearAdFree();

    /// <summary>Gets a one-line status such as "Next ad in 3m 21s".</summary>
    string GetStatusText();

    /// <summary>Describes the supported companion-mod integration features.</summary>
    AdvertisingIntegrationInfo GetIntegrationInfo();

    /// <summary>
    /// Opens the advertising mod's own advertisement/recipient picker. The main mod owns the UI and delivery;
    /// the companion mod only requests that the interface be opened.
    /// </summary>
    string? OpenAdvertisementSendMenu(string requestingModId);
}

/// <summary>Capability information returned to companion mods during API discovery.</summary>
public sealed class AdvertisingIntegrationInfo
{
    public string ApiVersion { get; set; } = "2";
    public bool CanOpenAdvertisementSendMenu { get; set; } = true;
    public bool CanSendToConnectedPlayers { get; set; } = true;
    public bool RecipientsMustHaveAdvertisingInstalled { get; set; } = true;
    public string PopupPresentation { get; set; } = "Normal Bentley's Advertising popup";
    public string DeliveryAuthority { get; set; } = "Host";
}

public sealed class BentleysAdvertisingApi : IBentleysAdvertisingApi
{
    private readonly ModEntry Mod;

    public BentleysAdvertisingApi(ModEntry mod)
    {
        this.Mod = mod;
    }

    public IReadOnlyList<string> GetAdvertiserIds() => this.Mod.ApiGetAdvertiserIds();
    public string GetAdvertiserName(string advertiserId) => this.Mod.ApiGetAdvertiserName(advertiserId);
    public int GetPopupCount(string advertiserId) => this.Mod.ApiGetPopupCount(advertiserId);
    public int GetLetterCount(string advertiserId) => this.Mod.ApiGetLetterCount(advertiserId);
    public string? ForcePopup(string advertiserId) => this.Mod.ApiForcePopup(advertiserId);
    public string? ForceLetter(string advertiserId, bool deliverNow) => this.Mod.ApiForceLetter(advertiserId, deliverNow);
    public void ClearAdFree() => this.Mod.ApiClearAdFree();
    public string GetStatusText() => this.Mod.ApiGetStatusText();
    public AdvertisingIntegrationInfo GetIntegrationInfo() => this.Mod.ApiGetIntegrationInfo();
    public string? OpenAdvertisementSendMenu(string requestingModId) => this.Mod.ApiOpenAdvertisementSendMenu(requestingModId);
}
