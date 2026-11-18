using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace BentleyPlayz.Advertising;

/// <summary>
/// The subscription shop, built as a real vanilla menu so touch input, UI scaling and the
/// dialogue-box frame all behave like the rest of the game.
/// Tap an offer to select it, then press Buy. (Two steps so a stray touch can never spend 25,000g.)
/// It also shows how long until the next ad, or how long the current ad-free period has left.
/// </summary>
internal sealed class SubscriptionMenu : IClickableMenu
{
    private const int SidePadding = 36;
    private const int MessageDurationMs = 4000;

    private readonly List<SubscriptionOffer> Offers;
    private readonly Func<bool> IsPaused;
    private readonly Func<string> GetRemainingText;
    private readonly Func<string> GetNextAdText;
    private readonly Func<SubscriptionOffer, bool> TryPurchase;

    private bool Compact;
    private int RowHeight = 96;
    private int RowGap = 8;
    private int HeaderHeight = 180;
    private int FooterHeight = 136;
    private int ButtonHeight = 64;

    private int FirstRow;
    private int VisibleRows;
    private int SelectedIndex = -1;
    private Rectangle BuyButton;
    private ClickableTextureComponent? UpArrow;
    private ClickableTextureComponent? DownArrow;

    private string? FeedbackText;
    private bool FeedbackIsError;
    private int FeedbackTimerMs;

    private int DragStartY = int.MinValue;
    private int DragStartFirstRow;

    public SubscriptionMenu(
        List<SubscriptionOffer> offers,
        Func<bool> isPaused,
        Func<string> getRemainingText,
        Func<string> getNextAdText,
        Func<SubscriptionOffer, bool> tryPurchase)
        : base(0, 0, 0, 0, true)
    {
        this.Offers = offers;
        this.IsPaused = isPaused;
        this.GetRemainingText = getRemainingText;
        this.GetNextAdText = getNextAdText;
        this.TryPurchase = tryPurchase;
        this.Layout();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.Layout();
    }

    private void Layout()
    {
        int viewWidth = Game1.uiViewport.Width;
        int viewHeight = Game1.uiViewport.Height;

        // Smaller spacing on short (phone) screens so more offers fit before scrolling is needed.
        // Rows are sized from the font so two lines of text always fit inside the frame.
        this.Compact = viewHeight < 700;
        this.RowHeight = Game1.smallFont.LineSpacing * 2 + 28;
        this.RowGap = this.Compact ? 6 : 8;
        this.HeaderHeight = this.Compact ? 156 : 180;
        this.ButtonHeight = this.Compact ? 56 : 64;
        this.FooterHeight = this.Compact ? 120 : 136;

        this.width = Math.Min(860, viewWidth - 32);
        int maxHeight = viewHeight - 32;
        int perRow = this.RowHeight + this.RowGap;

        int fit = Math.Max(1, (maxHeight - this.HeaderHeight - this.FooterHeight) / perRow);
        this.VisibleRows = Math.Max(1, Math.Min(this.Offers.Count, fit));
        this.height = this.HeaderHeight + this.VisibleRows * perRow + this.FooterHeight;

        this.xPositionOnScreen = (viewWidth - this.width) / 2;
        this.yPositionOnScreen = Math.Max(8, (viewHeight - this.height) / 2);

        this.FirstRow = Math.Clamp(this.FirstRow, 0, Math.Max(0, this.Offers.Count - this.VisibleRows));

        this.upperRightCloseButton = new ClickableTextureComponent(
            "exit",
            new Rectangle(this.xPositionOnScreen + this.width - 36, this.yPositionOnScreen - 8, 48, 48),
            null,
            null,
            Game1.mouseCursors,
            new Rectangle(337, 494, 12, 12),
            4f);

        int footerTop = this.yPositionOnScreen + this.HeaderHeight + this.VisibleRows * perRow;
        int controlsY = footerTop + this.FooterHeight - this.ButtonHeight - 28;
        bool needsArrows = this.Offers.Count > this.VisibleRows;
        int reserve = needsArrows ? 112 : 0;

        int buttonWidth = Math.Clamp(this.width - 2 * (SidePadding + reserve), 160, 300);
        this.BuyButton = new Rectangle(
            this.xPositionOnScreen + (this.width - buttonWidth) / 2,
            controlsY,
            buttonWidth,
            this.ButtonHeight);

        if (needsArrows)
        {
            int arrowY = controlsY + (this.ButtonHeight - 48) / 2;
            int downX = this.xPositionOnScreen + this.width - SidePadding - 44;
            this.DownArrow = new ClickableTextureComponent(
                new Rectangle(downX, arrowY, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 472, 11, 12),
                4f);
            this.UpArrow = new ClickableTextureComponent(
                new Rectangle(downX - 52, arrowY, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 459, 11, 12),
                4f);
        }
        else
        {
            this.UpArrow = null;
            this.DownArrow = null;
        }
    }

    private Rectangle GetRowBounds(int visibleIndex)
    {
        return new Rectangle(
            this.xPositionOnScreen + SidePadding,
            this.yPositionOnScreen + this.HeaderHeight + visibleIndex * (this.RowHeight + this.RowGap),
            this.width - SidePadding * 2,
            this.RowHeight);
    }

    private Rectangle GetListArea()
    {
        return new Rectangle(
            this.xPositionOnScreen + SidePadding,
            this.yPositionOnScreen + this.HeaderHeight,
            this.width - SidePadding * 2,
            this.VisibleRows * (this.RowHeight + this.RowGap));
    }

    private void ShowFeedback(string text, bool isError)
    {
        this.FeedbackText = text;
        this.FeedbackIsError = isError;
        this.FeedbackTimerMs = MessageDurationMs;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        // The base class handles the upper-right close button.
        base.receiveLeftClick(x, y, playSound);

        if (this.UpArrow?.containsPoint(x, y) == true)
        {
            this.Scroll(-1);
            return;
        }

        if (this.DownArrow?.containsPoint(x, y) == true)
        {
            this.Scroll(1);
            return;
        }

        if (this.BuyButton.Contains(x, y))
        {
            this.PressBuy();
            return;
        }

        if (this.GetListArea().Contains(x, y))
        {
            // Remember where the touch started so dragging the list scrolls it.
            this.DragStartY = y;
            this.DragStartFirstRow = this.FirstRow;
        }

        for (int i = 0; i < this.VisibleRows; i++)
        {
            int index = this.FirstRow + i;
            if (index >= this.Offers.Count)
                break;

            if (this.GetRowBounds(i).Contains(x, y))
            {
                if (this.SelectedIndex != index)
                    Game1.playSound("smallSelect");
                this.SelectedIndex = index;
                this.FeedbackText = null;
                return;
            }
        }
    }

    public override void leftClickHeld(int x, int y)
    {
        base.leftClickHeld(x, y);

        if (this.DragStartY == int.MinValue || this.Offers.Count <= this.VisibleRows)
            return;

        int steps = (this.DragStartY - y) / (this.RowHeight + this.RowGap);
        int max = Math.Max(0, this.Offers.Count - this.VisibleRows);
        this.FirstRow = Math.Clamp(this.DragStartFirstRow + steps, 0, max);
    }

    public override void releaseLeftClick(int x, int y)
    {
        base.releaseLeftClick(x, y);
        this.DragStartY = int.MinValue;
    }

    private void PressBuy()
    {
        if (this.SelectedIndex < 0 || this.SelectedIndex >= this.Offers.Count)
        {
            Game1.playSound("cancel");
            this.ShowFeedback("Select an offer first.", true);
            return;
        }

        SubscriptionOffer offer = this.Offers[this.SelectedIndex];
        int money = Game1.player.Money;
        if (money < offer.Price)
        {
            Game1.playSound("cancel");
            this.ShowFeedback($"Not enough gold - you need {offer.Price - money:N0}g more.", true);
            return;
        }

        if (this.TryPurchase(offer))
        {
            Game1.playSound("purchaseClick");
            this.ShowFeedback($"Purchased {offer.DisplayName}!", false);
        }
        else
        {
            Game1.playSound("cancel");
            this.ShowFeedback("That purchase couldn't be completed.", true);
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        this.Scroll(direction > 0 ? -1 : 1);
    }

    private void Scroll(int delta)
    {
        int max = Math.Max(0, this.Offers.Count - this.VisibleRows);
        int next = Math.Clamp(this.FirstRow + delta, 0, max);
        if (next != this.FirstRow)
        {
            this.FirstRow = next;
            Game1.playSound("shwip");
        }
    }

    public override void update(GameTime time)
    {
        base.update(time);

        if (this.FeedbackTimerMs > 0)
        {
            this.FeedbackTimerMs -= (int)time.ElapsedGameTime.TotalMilliseconds;
            if (this.FeedbackTimerMs <= 0)
                this.FeedbackText = null;
        }
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

        IClickableMenu.drawTextureBox(
            b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        // Title: vanilla pixel font, falling back to the small font on very narrow screens.
        const string title = "Advertising Subscriptions";
        int maxTitleWidth = this.width - 140;
        if (SpriteText.getWidthOfString(title) <= maxTitleWidth)
        {
            SpriteText.drawStringHorizontallyCenteredAt(b, title, this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen + 30);
        }
        else
        {
            Vector2 size = Game1.dialogueFont.MeasureString(title);
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont,
                new Vector2(this.xPositionOnScreen + (this.width - Math.Min(size.X, maxTitleWidth)) / 2f, this.yPositionOnScreen + 30),
                Game1.textColor);
        }

        // Header. Line 1: status on the left, your gold on the right. Line 2: time until the next ad.
        // (Everything uses the same small font so the text looks consistent.)
        int textMaxWidth = this.width - SidePadding * 2;
        int statusY = this.yPositionOnScreen + (this.Compact ? 84 : 96);

        string gold = $"Your gold: {Game1.player.Money:N0}g";
        Vector2 goldSize = Game1.smallFont.MeasureString(gold);
        Utility.drawTextWithShadow(b, gold, Game1.smallFont,
            new Vector2(this.xPositionOnScreen + this.width - SidePadding - goldSize.X, statusY), Game1.textColor);

        string status;
        Color statusColor = Game1.textColor;
        if (this.IsPaused())
        {
            string remaining = this.GetRemainingText();
            status = remaining.Length > 0 ? $"Ad-free! {remaining}" : "Advertisements are currently paused.";
            statusColor = Color.DarkGreen;
        }
        else
        {
            status = "Pick an offer, then press Buy.";
        }

        Utility.drawTextWithShadow(b, TrimToWidth(status, Game1.smallFont, textMaxWidth - (int)goldSize.X - 16), Game1.smallFont,
            new Vector2(this.xPositionOnScreen + SidePadding, statusY), statusColor);

        string nextAd = this.GetNextAdText();
        if (nextAd.Length > 0)
        {
            Utility.drawTextWithShadow(b, TrimToWidth(nextAd, Game1.smallFont, textMaxWidth), Game1.smallFont,
                new Vector2(this.xPositionOnScreen + SidePadding, statusY + Game1.smallFont.LineSpacing), Game1.textColor);
        }

        // Offers.
        if (this.Offers.Count == 0)
        {
            Utility.drawTextWithShadow(b, "No subscription offers are available.", Game1.smallFont,
                new Vector2(this.xPositionOnScreen + SidePadding, this.yPositionOnScreen + this.HeaderHeight + 12), Game1.textColor);
        }

        int mouseX = Game1.getMouseX(true);
        int mouseY = Game1.getMouseY(true);
        for (int i = 0; i < this.VisibleRows; i++)
        {
            int index = this.FirstRow + i;
            if (index >= this.Offers.Count)
                break;

            SubscriptionOffer offer = this.Offers[index];
            Rectangle row = this.GetRowBounds(i);
            bool canAfford = Game1.player.Money >= offer.Price;
            bool selected = index == this.SelectedIndex;
            bool hovered = row.Contains(mouseX, mouseY);

            Color rowTint = selected ? Color.LightGreen : hovered ? Color.Wheat : Color.White;
            IClickableMenu.drawTextureBox(
                b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                row.X, row.Y, row.Width, row.Height, rowTint, 4f, false);
            if (selected)
                DrawOutline(b, row, 4, Color.DarkGreen);

            int innerLeft = row.X + 20;
            int innerRight = row.Right - 20;
            int lineHeight = Game1.smallFont.LineSpacing;

            string priceText = $"{offer.Price:N0}g";
            Vector2 priceSize = Game1.smallFont.MeasureString(priceText);
            Utility.drawTextWithShadow(b, priceText, Game1.smallFont,
                new Vector2(innerRight - priceSize.X, row.Y + 12),
                canAfford ? new Color(60, 110, 40) : Color.Firebrick);

            int nameWidth = Math.Max(40, innerRight - innerLeft - (int)priceSize.X - 16);
            Utility.drawTextWithShadow(b, TrimToWidth(offer.DisplayName, Game1.smallFont, nameWidth), Game1.smallFont,
                new Vector2(innerLeft, row.Y + 12), Game1.textColor);

            string duration = $"Lasts {FormatDuration(offer)}";
            if (!canAfford)
                duration += "  -  not enough gold";
            Utility.drawTextWithShadow(b, TrimToWidth(duration, Game1.smallFont, innerRight - innerLeft), Game1.smallFont,
                new Vector2(innerLeft, row.Y + 12 + lineHeight), Game1.textColor * 0.85f);
        }

        // Footer: feedback line, Buy button, scroll arrows.
        int footerTop = this.yPositionOnScreen + this.HeaderHeight + this.VisibleRows * (this.RowHeight + this.RowGap);
        string? footerText = null;
        Color footerColor = Game1.textColor;
        if (this.FeedbackText is not null)
        {
            footerText = this.FeedbackText;
            footerColor = this.FeedbackIsError ? Color.Firebrick : Color.DarkGreen;
        }
        else if (this.SelectedIndex >= 0 && this.SelectedIndex < this.Offers.Count)
        {
            SubscriptionOffer picked = this.Offers[this.SelectedIndex];
            footerText = $"Selected: {picked.DisplayName} - {picked.Price:N0}g";
        }

        if (footerText is not null)
        {
            footerText = TrimToWidth(footerText, Game1.smallFont, textMaxWidth);
            Vector2 footerSize = Game1.smallFont.MeasureString(footerText);
            Utility.drawTextWithShadow(b, footerText, Game1.smallFont,
                new Vector2(this.xPositionOnScreen + (this.width - footerSize.X) / 2f, footerTop), footerColor);
        }

        bool hasSelection = this.SelectedIndex >= 0 && this.SelectedIndex < this.Offers.Count;
        bool canBuy = hasSelection && Game1.player.Money >= this.Offers[this.SelectedIndex].Price;
        string buyLabel = !hasSelection ? "Buy" : canBuy ? $"Buy for {this.Offers[this.SelectedIndex].Price:N0}g" : "Not enough gold";
        bool buyHovered = canBuy && this.BuyButton.Contains(mouseX, mouseY);

        IClickableMenu.drawTextureBox(
            b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            this.BuyButton.X, this.BuyButton.Y, this.BuyButton.Width, this.BuyButton.Height,
            canBuy ? (buyHovered ? Color.Wheat : Color.LightGreen) : Color.Gray, 4f, false);

        string trimmedBuy = TrimToWidth(buyLabel, Game1.smallFont, this.BuyButton.Width - 24);
        Vector2 buySize = Game1.smallFont.MeasureString(trimmedBuy);
        Utility.drawTextWithShadow(b, trimmedBuy, Game1.smallFont,
            new Vector2(this.BuyButton.X + (this.BuyButton.Width - buySize.X) / 2f, this.BuyButton.Y + (this.BuyButton.Height - buySize.Y) / 2f),
            canBuy ? Game1.textColor : Game1.textColor * 0.6f);

        this.UpArrow?.draw(b, this.FirstRow > 0 ? Color.White : Color.Gray, 0.88f);
        this.DownArrow?.draw(b, this.FirstRow < this.Offers.Count - this.VisibleRows ? Color.White : Color.Gray, 0.88f);

        this.upperRightCloseButton?.draw(b);
        this.drawMouse(b);
    }

    private static void DrawOutline(SpriteBatch b, Rectangle area, int thickness, Color color)
    {
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Y, area.Width, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Bottom - thickness, area.Width, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Y, thickness, area.Height), color);
        b.Draw(Game1.staminaRect, new Rectangle(area.Right - thickness, area.Y, thickness, area.Height), color);
    }

    /// <summary>Plain-language length of an offer, built from its numbers so it can't disagree with the price list.</summary>
    internal static string FormatDuration(SubscriptionOffer offer)
    {
        List<string> parts = new();

        if (offer.DurationMinutes > 0)
        {
            if (offer.DurationMinutes % 60 == 0)
            {
                int hours = offer.DurationMinutes / 60;
                parts.Add(hours == 1 ? "1 real-world hour" : $"{hours} real-world hours");
            }
            else
                parts.Add($"{offer.DurationMinutes} real-world minutes");
        }

        if (offer.DurationInGameDays > 0)
            parts.Add(offer.DurationInGameDays == 1 ? "1 in-game day" : $"{offer.DurationInGameDays} in-game days");

        return parts.Count > 0 ? string.Join(" + ", parts) : "an unknown time";
    }

    /// <summary>Shortens text with an ellipsis so it can never spill past its box.</summary>
    internal static string TrimToWidth(string text, SpriteFont font, int maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth)
            return text;

        string trimmed = text;
        while (trimmed.Length > 1 && font.MeasureString(trimmed + "...").X > maxWidth)
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        return trimmed.TrimEnd() + "...";
    }
}
