using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace BentleyPlayz.Advertising;

/// <summary>
/// The one-time welcome screen. The text is split into pages that are sized from the real screen and font, so
/// nothing is ever clipped or overlapping on a short phone screen; on a tall screen it simply fits on one page.
/// </summary>
internal sealed class WelcomeMenu : IClickableMenu
{
    private const int BlockGap = 14;

    private sealed class Block
    {
        public string Heading = "";
        public List<string> Lines = new();
        public int Height;
    }

    private readonly List<(string Heading, string Text)> Content = new();
    private readonly List<List<Block>> Pages = new();

    private int Page;
    private int Pad = 40;
    private int TitleHeight = 104;
    private int ButtonHeight = 64;
    private int FooterHeight = 100;
    private int BodyHeight;
    private Rectangle BackButton;
    private Rectangle NextButton;

    public WelcomeMenu(string? openMenuKeyName)
        : base(0, 0, 0, 0, true)
    {
        string openText = "Tap the gold AD button on your screen to open the AD menu. It shows when the next ad is due, your gold, and the price and length of every offer.";
        if (Constants.TargetPlatform != GamePlatform.Android && openMenuKeyName is not null)
            openText += $" On a keyboard you can also press {openMenuKeyName}.";

        this.Content.Add(("Thank you!",
            "Thanks for installing Bentley's Advertising. Pelican Town's businesses are now competing for your attention. Here is everything you need to know."));
        this.Content.Add(("Random advertisements",
            "Every few minutes a random ad from a local business pops up for a few seconds. You stay in full control of your farmer while it shows. Advertising letters may also arrive in your mailbox."));
        this.Content.Add(("Ad-free subscriptions",
            "Tired of ads? Spend your hard-earned gold on a temporary ad-free subscription, from one hour of play time up to a whole season. Longer passes cost a lot more, so choose wisely. You always get a confirmation before any gold is spent."));
        this.Content.Add(("Opening the AD menu", openText));
        this.Content.Add(("Moving the AD button",
            "Press and hold the AD button, then drag it anywhere you like. It remembers its spot. Use Reset AD button in the AD menu to send it back to the bottom-right corner."));

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
        bool compact = viewHeight < 700;

        this.width = Math.Min(860, viewWidth - 32);
        this.Pad = viewWidth < 700 ? 24 : 40;
        this.TitleHeight = compact ? 84 : 104;
        this.ButtonHeight = compact ? 56 : 64;
        this.FooterHeight = this.ButtonHeight + (compact ? 28 : 40);

        int textWidth = this.width - this.Pad * 2;
        int lineHeight = Game1.smallFont.LineSpacing;
        int maxBodyHeight = Math.Max(lineHeight * 4, viewHeight - 16 - this.TitleHeight - this.FooterHeight - 12);

        // Wrap every block, then pack blocks onto pages so no page is taller than the screen allows.
        List<Block> blocks = new();
        foreach ((string heading, string text) in this.Content)
        {
            Block block = new() { Heading = heading };
            block.Lines.AddRange(Game1.parseText(text, Game1.smallFont, textWidth)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));
            block.Height = lineHeight + block.Lines.Count * lineHeight + BlockGap;
            blocks.Add(block);
        }

        this.Pages.Clear();
        List<Block> current = new();
        int currentHeight = 0;
        this.BodyHeight = 0;
        foreach (Block block in blocks)
        {
            if (current.Count > 0 && currentHeight + block.Height > maxBodyHeight)
            {
                this.Pages.Add(current);
                this.BodyHeight = Math.Max(this.BodyHeight, currentHeight);
                current = new List<Block>();
                currentHeight = 0;
            }

            current.Add(block);
            currentHeight += block.Height;
        }

        if (current.Count > 0)
        {
            this.Pages.Add(current);
            this.BodyHeight = Math.Max(this.BodyHeight, currentHeight);
        }

        this.Page = Math.Clamp(this.Page, 0, Math.Max(0, this.Pages.Count - 1));

        this.height = this.TitleHeight + this.BodyHeight + this.FooterHeight;
        this.xPositionOnScreen = (viewWidth - this.width) / 2;
        this.yPositionOnScreen = Math.Max(8, (viewHeight - this.height) / 2);

        this.upperRightCloseButton = new ClickableTextureComponent(
            "exit",
            new Rectangle(this.xPositionOnScreen + this.width - 36, this.yPositionOnScreen - 8, 48, 48),
            null,
            null,
            Game1.mouseCursors,
            new Rectangle(337, 494, 12, 12),
            4f);

        int buttonWidth = Math.Clamp((this.width - this.Pad * 2) / 3, 130, 220);
        int buttonY = this.yPositionOnScreen + this.height - this.ButtonHeight - (compact ? 20 : 28);
        int left = this.xPositionOnScreen + this.Pad;
        int right = this.xPositionOnScreen + this.width - this.Pad;

        this.BackButton = new Rectangle(left, buttonY, buttonWidth, this.ButtonHeight);
        this.NextButton = this.Pages.Count > 1
            ? new Rectangle(right - buttonWidth, buttonY, buttonWidth, this.ButtonHeight)
            : new Rectangle(this.xPositionOnScreen + (this.width - buttonWidth) / 2, buttonY, buttonWidth, this.ButtonHeight);
    }

    private bool IsLastPage => this.Page >= this.Pages.Count - 1;

    private void Advance()
    {
        if (!this.IsLastPage)
        {
            this.Page++;
            Game1.playSound("shwip");
            return;
        }

        Game1.playSound("bigDeSelect");
        this.exitThisMenu();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Enter || key == Keys.Space)
        {
            this.Advance();
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        // The base class handles the upper-right close button.
        base.receiveLeftClick(x, y, playSound);

        if (this.NextButton.Contains(x, y))
        {
            this.Advance();
            return;
        }

        if (this.Pages.Count > 1 && this.Page > 0 && this.BackButton.Contains(x, y))
        {
            this.Page--;
            Game1.playSound("shwip");
        }
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);

        IClickableMenu.drawTextureBox(
            b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        // Title: vanilla pixel font, falling back to a regular font on narrow screens.
        const string title = "Welcome to Bentley's Advertising!";
        int maxTitleWidth = this.width - 140;
        float titleY = this.yPositionOnScreen + (this.TitleHeight < 100 ? 26 : 32);
        if (SpriteText.getWidthOfString(title) <= maxTitleWidth)
        {
            SpriteText.drawStringHorizontallyCenteredAt(b, title, this.xPositionOnScreen + this.width / 2, (int)titleY);
        }
        else
        {
            SpriteFont titleFont = Game1.dialogueFont.MeasureString(title).X <= maxTitleWidth ? Game1.dialogueFont : Game1.smallFont;
            string fitted = SubscriptionMenu.TrimToWidth(title, titleFont, maxTitleWidth);
            Vector2 size = titleFont.MeasureString(fitted);
            Utility.drawTextWithShadow(b, fitted, titleFont,
                new Vector2(this.xPositionOnScreen + (this.width - size.X) / 2f, titleY), Game1.textColor);
        }

        int left = this.xPositionOnScreen + this.Pad;
        int lineHeight = Game1.smallFont.LineSpacing;

        // Divider under the title.
        b.Draw(Game1.staminaRect,
            new Rectangle(left, this.yPositionOnScreen + this.TitleHeight - 12, this.width - this.Pad * 2, 4),
            new Color(120, 70, 40) * 0.5f);

        // Current page.
        float y = this.yPositionOnScreen + this.TitleHeight;
        if (this.Pages.Count > 0)
        {
            foreach (Block block in this.Pages[this.Page])
            {
                Utility.drawTextWithShadow(b, block.Heading, Game1.smallFont, new Vector2(left, y), new Color(140, 30, 20));
                y += lineHeight;
                foreach (string line in block.Lines)
                {
                    Utility.drawTextWithShadow(b, line, Game1.smallFont, new Vector2(left, y), Game1.textColor);
                    y += lineHeight;
                }

                y += BlockGap;
            }
        }

        int mouseX = Game1.getMouseX(true);
        int mouseY = Game1.getMouseY(true);

        // Footer: Back, page indicator, Next / Continue.
        if (this.Pages.Count > 1)
        {
            if (this.Page > 0)
                DrawButton(b, this.BackButton, "Back", this.BackButton.Contains(mouseX, mouseY) ? Color.Wheat : Color.White);

            string indicator = $"{this.Page + 1} / {this.Pages.Count}";
            Vector2 indicatorSize = Game1.smallFont.MeasureString(indicator);
            Utility.drawTextWithShadow(b, indicator, Game1.smallFont,
                new Vector2(this.xPositionOnScreen + (this.width - indicatorSize.X) / 2f,
                    this.BackButton.Y + (this.BackButton.Height - indicatorSize.Y) / 2f),
                Game1.textColor * 0.85f);
        }

        DrawButton(b, this.NextButton, this.IsLastPage ? "Continue" : "Next",
            this.NextButton.Contains(mouseX, mouseY) ? Color.Wheat : Color.LightGreen);

        this.upperRightCloseButton?.draw(b);
        this.drawMouse(b);
    }

    private static void DrawButton(SpriteBatch b, Rectangle area, string label, Color tint)
    {
        IClickableMenu.drawTextureBox(
            b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            area.X, area.Y, area.Width, area.Height, tint, 4f, false);

        string text = SubscriptionMenu.TrimToWidth(label, Game1.smallFont, area.Width - 24);
        Vector2 size = Game1.smallFont.MeasureString(text);
        Utility.drawTextWithShadow(b, text, Game1.smallFont,
            new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f),
            Game1.textColor);
    }
}
