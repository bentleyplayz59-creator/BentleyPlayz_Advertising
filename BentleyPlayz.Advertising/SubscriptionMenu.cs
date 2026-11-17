using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace BentleyPlayz.Advertising;

/// <summary>
/// The subscription shop, built as a real vanilla menu so touch input, UI scaling and the
/// dialogue-box frame all behave like the rest of the game.
/// </summary>
internal sealed class SubscriptionMenu : IClickableMenu
{
    private const int RowHeight = 96;
    private const int RowGap = 8;
    private const int HeaderHeight = 150;
    private const int FooterHeight = 36;
    private const int SidePadding = 36;

    private readonly List<SubscriptionOffer> Offers;
    private readonly Func<bool> IsPaused;
    private readonly Func<SubscriptionOffer, bool> TryPurchase;

    private int FirstRow;
    private int VisibleRows;
    private ClickableTextureComponent? UpArrow;
    private ClickableTextureComponent? DownArrow;

    public SubscriptionMenu(List<SubscriptionOffer> offers, Func<bool> isPaused, Func<SubscriptionOffer, bool> tryPurchase)
        : base(0, 0, 0, 0, true)
    {
        this.Offers = offers;
        this.IsPaused = isPaused;
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

        this.width = Math.Min(860, viewWidth - 48);
        int maxHeight = viewHeight - 48;
        int perRow = RowHeight + RowGap;

        int fit = Math.Max(1, (maxHeight - HeaderHeight - FooterHeight) / perRow);
        this.VisibleRows = Math.Max(1, Math.Min(this.Offers.Count, fit));
        this.height = HeaderHeight + this.VisibleRows * perRow + FooterHeight;

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

        if (this.Offers.Count > this.VisibleRows)
        {
            int arrowY = this.yPositionOnScreen + 92;
            this.UpArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen + this.width - 140, arrowY, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 459, 11, 12),
                4f);
            this.DownArrow = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen + this.width - 84, arrowY, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 472, 11, 12),
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
            this.yPositionOnScreen + HeaderHeight + visibleIndex * (RowHeight + RowGap),
            this.width - SidePadding * 2,
            RowHeight);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
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

        for (int i = 0; i < this.VisibleRows; i++)
        {
            int index = this.FirstRow + i;
            if (index >= this.Offers.Count)
                break;

            if (this.GetRowBounds(i).Contains(x, y))
            {
                if (this.TryPurchase(this.Offers[index]))
                    Game1.playSound("purchaseClick");
                else
                    Game1.playSound("cancel");
                return;
            }
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

        string status = this.IsPaused() ? "Advertisements are currently paused." : "Choose an offer to pause advertisements.";
        status = TrimToWidth(status, Game1.smallFont, this.width - SidePadding * 2 - (this.UpArrow is null ? 0 : 130));
        Utility.drawTextWithShadow(b, status, Game1.smallFont,
            new Vector2(this.xPositionOnScreen + SidePadding, this.yPositionOnScreen + 100), Game1.textColor);

        for (int i = 0; i < this.VisibleRows; i++)
        {
            int index = this.FirstRow + i;
            if (index >= this.Offers.Count)
                break;

            SubscriptionOffer offer = this.Offers[index];
            Rectangle row = this.GetRowBounds(i);
            bool canAfford = Game1.player.Money >= offer.Price;
            bool hovered = row.Contains(Game1.getMouseX(true), Game1.getMouseY(true));

            IClickableMenu.drawTextureBox(
                b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                row.X, row.Y, row.Width, row.Height,
                hovered && canAfford ? Color.Wheat : Color.White, 4f, false);

            int textWidth = row.Width - 40;
            string heading = TrimToWidth($"{offer.DisplayName} - {offer.Price:N0}g", Game1.smallFont, textWidth);
            Utility.drawTextWithShadow(b, heading, Game1.smallFont,
                new Vector2(row.X + 20, row.Y + 12), canAfford ? Game1.textColor : Color.Firebrick);

            string description = TrimToWidth(offer.Description, Game1.tinyFont, textWidth);
            Utility.drawTextWithShadow(b, description, Game1.tinyFont,
                new Vector2(row.X + 20, row.Y + 12 + Game1.smallFont.LineSpacing + 4), Game1.textColor * 0.8f);
        }

        this.UpArrow?.draw(b, this.FirstRow > 0 ? Color.White : Color.Gray, 0.88f);
        this.DownArrow?.draw(b, this.FirstRow < this.Offers.Count - this.VisibleRows ? Color.White : Color.Gray, 0.88f);

        this.upperRightCloseButton?.draw(b);
        this.drawMouse(b);
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
