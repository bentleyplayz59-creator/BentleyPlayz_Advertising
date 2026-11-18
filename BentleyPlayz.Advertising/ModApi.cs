namespace BentleyPlayz.Advertising;

/// <summary>
/// Public API other SMAPI mods (such as Bentley's Advertising Dev Tools) can request through
/// <c>Helper.ModRegistry.GetApi&lt;T&gt;("BentleyPlayz.Advertising")</c>. Copy this interface into your own mod.
/// Methods that can fail return an error message, or null on success.
/// </summary>
public interface IBentleysAdvertisingApi
{
    /// <summary>The ids of every advertiser in advertisements.json.</summary>
    IReadOnlyList<string> GetAdvertiserIds();

    /// <summary>The display name of an advertiser (e.g. "PIERRE'S GENERAL STORE"), or the id if unknown.</summary>
    string GetAdvertiserName(string advertiserId);

    /// <summary>How many popup messages an advertiser has.</summary>
    int GetPopupCount(string advertiserId);

    /// <summary>How many letters an advertiser has.</summary>
    int GetLetterCount(string advertiserId);

    /// <summary>Shows an advertisement popup from the given advertiser as soon as no menu is open. Works even while ad-free.</summary>
    string? ForcePopup(string advertiserId);

    /// <summary>Delivers a letter from the given advertiser, either into the mailbox right now or into tomorrow's mail.</summary>
    string? ForceLetter(string advertiserId, bool deliverNow);

    /// <summary>Removes any active ad-free subscription time in the current save.</summary>
    void ClearAdFree();

    /// <summary>A one-line status such as "Next ad in 3m 21s" or "Ad-free! 52m 10s left".</summary>
    string GetStatusText();
}

internal sealed class BentleysAdvertisingApi : IBentleysAdvertisingApi
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
}
