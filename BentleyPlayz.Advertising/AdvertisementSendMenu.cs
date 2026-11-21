using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace BentleyPlayz.Advertising;

/// <summary>
/// The advertising mod's own picker UI, opened by companion mods through the public API.
/// It lets the player choose an advertiser and one connected farmhand.
/// </summary>
internal sealed class AdvertisementSendMenu : IClickableMenu
{
    private const int RowHeight = 48;
    private const int VisibleRows = 6;
    private const int Padding = 24;

    private readonly List<(string Id, string Name)> Advertisers;
    private readonly List<(long Id, string Name)> Targets;
    private readonly Action<string, long> Send;
    private int SelectedAdvertiser = -1;
    private int SelectedTarget = -1;
    private int FirstAdvertiser;
    private int FirstTarget;
    private Rectangle AdvertiserPanel;
    private Rectangle TargetPanel;
    private Rectangle SendButton;
    private Rectangle CloseButton;

    public AdvertisementSendMenu(
        List<(string Id, string Name)> advertisers,
        List<(long Id, string Name)> targets,
        Action<string, long> send)
        : base(0, 0, 0, 0, true)
    {
        this.Advertisers = advertisers;
        this.Targets = targets;
        this.Send = send;
        this.Layout();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.Layout();
    }

    private void Layout()
    {
        this.width = Math.Min(820, Game1.uiViewport.Width - 24);
        this.height = Math.Min(500, Game1.uiViewport.Height - 24);
        this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
        this.yPositionOnScreen = (Game1.uiViewport.Height - this.height) / 2;

        int columnWidth = (this.width - Padding * 3) / 2;
        int panelY = this.yPositionOnScreen + 104;
        int panelHeight = VisibleRows * RowHeight + 16;
        this.AdvertiserPanel = new Rectangle(this.xPositionOnScreen + Padding, panelY, columnWidth, panelHeight);
        this.TargetPanel = new Rectangle(this.xPositionOnScreen + Padding * 2 + columnWidth, panelY, columnWidth, panelHeight);
        this.SendButton = new Rectangle(this.xPositionOnScreen + this.width - Padding - 190,
            this.yPositionOnScreen + this.height - 76, 190, 52);
        this.CloseButton = new Rectangle(this.xPositionOnScreen + this.width - 52, this.yPositionOnScreen + 12, 40, 40);
        this.FirstAdvertiser = Math.Clamp(this.FirstAdvertiser, 0, Math.Max(0, this.Advertisers.Count - VisibleRows));
        this.FirstTarget = Math.Clamp(this.FirstTarget, 0, Math.Max(0, this.Targets.Count - VisibleRows));
    }

    public override void receiveScrollWheelAction(int direction)
    {
        Point cursor = new(Game1.getMouseX(true), Game1.getMouseY(true));
        if (this.AdvertiserPanel.Contains(cursor))
            this.FirstAdvertiser = Math.Clamp(this.FirstAdvertiser + (direction < 0 ? 1 : -1), 0, Math.Max(0, this.Advertisers.Count - VisibleRows));
        else if (this.TargetPanel.Contains(cursor))
            this.FirstTarget = Math.Clamp(this.FirstTarget + (direction < 0 ? 1 : -1), 0, Math.Max(0, this.Targets.Count - VisibleRows));
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.CloseButton.Contains(x, y))
        {
            this.exitThisMenu();
            return;
        }

        for (int row = 0; row < VisibleRows; row++)
        {
            Rectangle advertiserRow = new(this.AdvertiserPanel.X + 8, this.AdvertiserPanel.Y + 8 + row * RowHeight,
                this.AdvertiserPanel.Width - 16, RowHeight - 4);
            int advertiserIndex = this.FirstAdvertiser + row;
            if (advertiserIndex < this.Advertisers.Count && advertiserRow.Contains(x, y))
            {
                this.SelectedAdvertiser = advertiserIndex;
                if (playSound) Game1.playSound("smallSelect");
                return;
            }

            Rectangle targetRow = new(this.TargetPanel.X + 8, this.TargetPanel.Y + 8 + row * RowHeight,
                this.TargetPanel.Width - 16, RowHeight - 4);
            int targetIndex = this.FirstTarget + row;
            if (targetIndex < this.Targets.Count && targetRow.Contains(x, y))
            {
                this.SelectedTarget = targetIndex;
                if (playSound) Game1.playSound("smallSelect");
                return;
            }
        }

        if (this.SendButton.Contains(x, y))
        {
            if (this.SelectedAdvertiser >= 0 && this.SelectedAdvertiser < this.Advertisers.Count
                && this.SelectedTarget >= 0 && this.SelectedTarget < this.Targets.Count)
            {
                var advertiser = this.Advertisers[this.SelectedAdvertiser];
                var target = this.Targets[this.SelectedTarget];
                this.exitThisMenu();
                this.Send(advertiser.Id, target.Id);
                Game1.playSound("bigSelect");
            }
            else
            {
                Game1.playSound("cancel");
            }
            return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.55f);
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        Utility.drawTextWithShadow(b, "Send an Advertisement", Game1.dialogueFont,
            new Vector2(this.xPositionOnScreen + Padding, this.yPositionOnScreen + 24), Game1.textColor);
        Utility.drawTextWithShadow(b, "Choose an advertiser and a connected player.", Game1.smallFont,
            new Vector2(this.xPositionOnScreen + Padding, this.yPositionOnScreen + 70), Game1.textColor);

        this.DrawList(b, this.AdvertiserPanel, "ADVERTISERS", this.Advertisers.Select(p => p.Name).ToList(),
            this.FirstAdvertiser, this.SelectedAdvertiser);
        this.DrawList(b, this.TargetPanel, "PLAYERS", this.Targets.Select(p => p.Name).ToList(),
            this.FirstTarget, this.SelectedTarget);

        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
            this.SendButton.X, this.SendButton.Y, this.SendButton.Width, this.SendButton.Height,
            this.SelectedAdvertiser >= 0 && this.SelectedTarget >= 0 ? Color.LightGreen : Color.Gray, 4f, false);
        string sendText = "Send advertisement";
        Vector2 sendSize = Game1.smallFont.MeasureString(sendText);
        Utility.drawTextWithShadow(b, sendText, Game1.smallFont,
            new Vector2(this.SendButton.Center.X - sendSize.X / 2, this.SendButton.Center.Y - sendSize.Y / 2),
            Game1.textColor);

        this.upperRightCloseButton?.draw(b);
        this.drawMouse(b);
    }

    private void DrawList(SpriteBatch b, Rectangle panel, string heading, List<string> names, int first, int selected)
    {
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            panel.X, panel.Y, panel.Width, panel.Height, Color.White);
        Utility.drawTextWithShadow(b, heading, Game1.smallFont,
            new Vector2(panel.X + 12, panel.Y - 28), Game1.textColor);

        for (int row = 0; row < VisibleRows; row++)
        {
            int index = first + row;
            if (index >= names.Count)
                break;

            Rectangle area = new(panel.X + 8, panel.Y + 8 + row * RowHeight, panel.Width - 16, RowHeight - 4);
            Color tint = index == selected ? Color.LightGreen : Color.White;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                area.X, area.Y, area.Width, area.Height, tint, 4f, false);
            string label = SubscriptionMenu.TrimToWidth(names[index], Game1.smallFont, area.Width - 20);
            Utility.drawTextWithShadow(b, label, Game1.smallFont,
                new Vector2(area.X + 10, area.Y + (area.Height - Game1.smallFont.LineSpacing) / 2), Game1.textColor);
        }

        if (names.Count == 0)
        {
            string empty = "None available";
            Utility.drawTextWithShadow(b, empty, Game1.smallFont,
                new Vector2(panel.X + 12, panel.Y + 16), Game1.textColor);
        }
    }
}
