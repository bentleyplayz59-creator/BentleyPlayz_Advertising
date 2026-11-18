using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace BentleyPlayz.Advertising;

/// <summary>
/// The subscription shop, built as a real vanilla menu so touch input, UI scaling and the
/// dialogue-box frame all behave like the rest of the game.
/// Tap an offer to select it, press Buy, then confirm. (Two taps plus a confirmation so a stray touch can never
/// spend a large amount of gold.) It also shows how long until the next ad, or how long the current ad-free period
/// has left, and holds the "Reset AD button" control.
/// </summary>
internal sealed class SubscriptionMenu : IClickableMenu
{
    private const int SidePadding = 36;
    private const int MessageDurationMs = 4000;
    private const string ResetLabel = "Reset AD button";

    private readonly List<SubscriptionOffer> Offers;
    private readonly Func<bool> IsPaused;
    private readonly Func<string> GetRemainingText;
    private readonly Func<string> GetNextAdText;
    private readonly Func<SubscriptionOffer, bool> TryPurchase;
    private readonly Action ResetButtonPosition;

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
    private Rectangle ResetButton;
    private ClickableTextureComponent? UpArrow;
    private ClickableTextureComponent? DownArrow;

    private string? FeedbackText;
    private bool FeedbackIsError;
    private int FeedbackTimerMs;

    private int DragStartY = int.MinValue;
    private int DragStartFirstRow;

    // Purchase confirmation overlay.
    private bool Confirming;
    private SubscriptionOffer? ConfirmOffer;
    private readonly List<(string Text, Color Color)> ConfirmLines = new();
    private Rectangle ConfirmPanel;
    private Rectangle ConfirmYes;
    private Rectangle ConfirmNo;

    public SubscriptionMenu(
        List<SubscriptionOffer> offers,
        Func<bool> isPaused,
        Func<string> getRemainingText,
        Func<string> getNextAdText,
        Func<SubscriptionOffer, bool> tryPurchase,
        Action resetButtonPosition)
        : base(0, 0, 0, 0, true)
    {
        this.Offers = offers;
        this.IsPaused = isPaused;
        this.GetRemainingText = getRemainingText;
        this.GetNextAdText = getNextAdText;
        this.TryPurchase = tryPurchase;
        this.ResetButtonPosition = resetButtonPosition;
        this.Layout();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.Layout();
    }

    public override bool readyToClose()
    {
        // While the confirmation is up, Escape / back cancels it instead of closing the whole menu.
        return !this.Confirming;
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

        // Footer controls row: [Reset AD button]  [  Buy  ]  [up][down]
        int footerTop = this.yPositionOnScreen + this.HeaderHeight + this.VisibleRows * perRow;
        int controlsY = footerTop + this.FooterHeight - this.ButtonHeight - 28;
        bool needsArrows = this.Offers.Count > this.VisibleRows;
        const int gap = 12;

        int left = this.xPositionOnScreen + SidePadding;
        int right = this.xPositionOnScreen + this.width - SidePadding;
        int inner = right - left;

        int resetWidth = Math.Min((int)Game1.smallFont.MeasureString(ResetLabel).X + 36, inner / 3);
        this.ResetButton = new Rectangle(left, controlsY, resetWidth, this.ButtonHeight);

        int rightReserve = needsArrows ? 100 : 0;
        int middleLeft = left + resetWidth + gap;
        int middleRight = right - rightReserve - (needsArrows ? gap : 0);
        int middleWidth = Math.Max(0, middleRight - middleLeft);
        int buttonWidth = Math.Clamp(middleWidth, 120, 300);
        this.BuyButton = new Rectangle(
            middleLeft + (middleWidth - buttonWidth) / 2,
            controlsY,
            buttonWidth,
            this.ButtonHeight);

        if (needsArrows)
        {
            int arrowY = controlsY + (this.ButtonHeight - 48) / 2;
            int downX = right - 44;
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

        if (this.Confirming)
            this.LayoutConfirm();
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

    /*********
    ** Purchase confirmation
    *********/
    private void OpenConfirm(SubscriptionOffer offer)
    {
        this.ConfirmOffer = offer;
        this.Confirming = true;
        this.LayoutConfirm();
    }

    private void AddConfirmLines(string text, int textWidth, Color color)
    {
        foreach (string line in Game1.parseText(text, Game1.smallFont, textWidth).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            this.ConfirmLines.Add((line.Trim(), color));
    }

    private void LayoutConfirm()
    {
        if (this.ConfirmOffer is null)
            return;

        SubscriptionOffer offer = this.ConfirmOffer;
        int money = Game1.player.Money;
        int viewWidth = Game1.uiViewport.Width;
        int viewHeight = Game1.uiViewport.Height;

        const int pad = 28;
        int panelWidth = Math.Min(viewWidth - 32, 560);
        int textWidth = panelWidth - pad * 2;

        this.ConfirmLines.Clear();
        this.AddConfirmLines($"Buy {offer.DisplayName}?", textWidth, Color.Firebrick);
        this.AddConfirmLines($"Ad-free for {FormatDuration(offer)}.", textWidth, Game1.textColor);
        this.AddConfirmLines($"Price: {offer.Price:N0}g", textWidth, Game1.textColor);
        this.AddConfirmLines($"You have {money:N0}g ({money - offer.Price:N0}g left after buying).", textWidth, Game1.textColor);
        if (this.IsPaused())
            this.AddConfirmLines("This is added to your current ad-free time.", textWidth, new Color(60, 110, 40));

        int linesHeight = this.ConfirmLines.Count * Game1.smallFont.LineSpacing;
        int panelHeight = pad + linesHeight + 18 + this.ButtonHeight + pad;

        int panelX = (viewWidth - panelWidth) / 2;
        int panelY = Math.Max(8, (viewHeight - panelHeight) / 2);
        this.ConfirmPanel = new Rectangle(panelX, panelY, panelWidth, panelHeight);

        int buttonWidth = (panelWidth - pad * 2 - 16) / 2;
        int buttonY = panelY + panelHeight - pad - this.ButtonHeight;
        this.ConfirmYes = new Rectangle(panelX + pad, buttonY, buttonWidth, this.ButtonHeight);
        this.ConfirmNo = new Rectangle(this.ConfirmYes.Right + 16, buttonY, buttonWidth, this.ButtonHeight);
    }

    private void CancelConfirm()
    {
        Game1.playSound("smallSelect");
        this.Confirming = false;
        this.ConfirmOffer = null;
    }

    private void ConfirmPurchase()
    {
        SubscriptionOffer? offer = this.ConfirmOffer;
        this.Confirming = false;
        this.ConfirmOffer = null;
        if (offer is null)
            return;

        // Re-check at the last moment, in case anything changed while the confirmation was open.
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
            this.ShowFeedback($"Purchased {offer.DisplayName}! Ad-free for {FormatDuration(offer)}.", false);
        }
        else
        {
            Game1.playSound("cancel");
            this.ShowFeedback("That purchase couldn't be completed.", true);
        }
    }

    /*********
    ** Input
    *********/
    public override void receiveKeyPress(Keys key)
    {
        if (this.Confirming && key == Keys.Escape)
        {
            this.CancelConfirm();
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.Confirming)
        {
            if (this.ConfirmYes.Contains(x, y))
                this.ConfirmPurchase();
            else if (this.ConfirmNo.Contains(x, y) || !this.ConfirmPanel.Contains(x, y))
                this.CancelConfirm();
            return;
        }

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

        if (this.ResetButton.Contains(x, y))
        {
            this.ResetButtonPosition();
            Game1.playSound("bigSelect");
            this.ShowFeedback("AD button moved back to the bottom-right corner.", false);
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

        if (this.Confirming || this.DragStartY == int.MinValue || this.Offers.Count <= this.VisibleRows)
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

        // Nothing is spent until the player confirms.
        Game1.playSound("smallSelect");
        this.OpenConfirm(offer);
    }

    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        if (!this.Confirming)
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

    /*********
    ** Drawing
    *********/
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
        bool interactive = !this.Confirming;
        for (int i = 0; i < this.VisibleRows; i++)
        {
            int index = this.FirstRow + i;
            if (index >= this.Offers.Count)
                break;

            SubscriptionOffer offer = this.Offers[index];
            Rectangle row = this.GetRowBounds(i);
            bool canAfford = Game1.player.Money >= offer.Price;
            bool selected = index == this.SelectedIndex;
            bool hovered = interactive && row.Contains(mouseX, mouseY);

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

        // Footer: feedback line, then the controls row (Reset AD button, Buy, scroll arrows).
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

        // Reset AD button.
        bool resetHovered = interactive && this.ResetButton.Contains(mouseX, mouseY);
        IClickableMenu.drawTextureBox(
            b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            this.ResetButton.X, this.ResetButton.Y, this.ResetButton.Width, this.ResetButton.Height,
            resetHovered ? Color.Wheat : Color.White, 4f, false);
        string resetText = TrimToWidth(ResetLabel, Game1.smallFont, this.ResetButton.Width - 20);
        Vector2 resetSize = Game1.smallFont.MeasureString(resetText);
        Utility.drawTextWithShadow(b, resetText, Game1.smallFont,
            new Vector2(this.ResetButton.X + (this.ResetButton.Width - resetSize.X) / 2f, this.ResetButton.Y + (this.ResetButton.Height - resetSize.Y) / 2f),
            Game1.textColor);

        // Buy button.
        bool hasSelection = this.SelectedIndex >= 0 && this.SelectedIndex < this.Offers.Count;
        bool canBuy = hasSelection && Game1.player.Money >= this.Offers[this.SelectedIndex].Price;
        string buyLabel = !hasSelection ? "Buy" : canBuy ? $"Buy for {this.Offers[this.SelectedIndex].Price:N0}g" : "Not enough gold";
        bool buyHovered = interactive && canBuy && this.BuyButton.Contains(mouseX, mouseY);

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

        if (this.Confirming)
        {
            this.DrawConfirm(b, mouseX, mouseY);
        }
        else
        {
            this.upperRightCloseButton?.draw(b);
        }

        this.drawMouse(b);
    }

    private void DrawConfirm(SpriteBatch b, int mouseX, int mouseY)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.55f);

        IClickableMenu.drawTextureBox(
            b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            this.ConfirmPanel.X, this.ConfirmPanel.Y, this.ConfirmPanel.Width, this.ConfirmPanel.Height, Color.White);

        float y = this.ConfirmPanel.Y + 28;
        foreach ((string text, Color color) in this.ConfirmLines)
        {
            Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(this.ConfirmPanel.X + 28, y), color);
            y += Game1.smallFont.LineSpacing;
        }

        DrawTextButton(b, this.ConfirmYes, "Yes, buy", this.ConfirmYes.Contains(mouseX, mouseY) ? Color.Wheat : Color.LightGreen);
        DrawTextButton(b, this.ConfirmNo, "Cancel", this.ConfirmNo.Contains(mouseX, mouseY) ? Color.Wheat : Color.White);
    }

    private static void DrawTextButton(SpriteBatch b, Rectangle area, string label, Color tint)
    {
        IClickableMenu.drawTextureBox(
            b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            area.X, area.Y, area.Width, area.Height, tint, 4f, false);

        string text = TrimToWidth(label, Game1.smallFont, area.Width - 24);
        Vector2 size = Game1.smallFont.MeasureString(text);
        Utility.drawTextWithShadow(b, text, Game1.smallFont,
            new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f),
            Game1.textColor);
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
                parts.Add(hours == 1 ? "1 hour of play time" : $"{hours} hours of play time");
            }
            else
                parts.Add($"{offer.DurationMinutes} minutes of play time");
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
