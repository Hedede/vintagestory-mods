using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace NB.Cartographer
{
    public class GuiDialogEditSharedWayPoint : GuiDialogGeneric
    {
        public override bool PrefersUngrabbedMouse => true;

        EnumDialogType dialogType = EnumDialogType.Dialog;
        public override EnumDialogType DialogType => dialogType;

        int[] colors;
        string[] icons;

        SharedWaypoint waypoint;

        public override double DrawOrder => 0.2;


        public GuiDialogEditSharedWayPoint(ICoreClientAPI capi, SharedWaypointMapLayer wml, SharedWaypoint waypoint) : base("", capi)
        {
            icons = wml.WaypointLayer.WaypointIcons.Keys.ToArray();
            colors = wml.WaypointLayer.WaypointColors.ToArray();

            this.waypoint = waypoint;

            ComposeDialog();
        }

        public override bool TryOpen()
        {
            ComposeDialog();
            return base.TryOpen();
        }

        private void ComposeDialog()
        {
            ElementBounds leftColumn = ElementBounds.Fixed(0, 28, 120, 25);
            ElementBounds rightColumn = leftColumn.RightCopy();

            ElementBounds buttonRow = ElementBounds.Fixed(0, 28, 360, 25);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(leftColumn, rightColumn);

            ElementBounds dialogBounds =
                ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);


            if (SingleComposer != null) SingleComposer.Dispose();

            int colorIconSize = 22;
            

            int iconIndex = icons.IndexOf(waypoint.Icon);
            if (iconIndex < 0) iconIndex = 0;

            int colorIndex = colors.IndexOf(waypoint.Color);
            if (colorIndex < 0)
            {
                colors = colors.Append(waypoint.Color);
                colorIndex = colors.Length - 1;
            }

            var owner = capi.World.PlayerByUid(waypoint.OwningPlayerUid);
            var ownerName = (owner?.PlayerName) ?? waypoint.PlayerName;

            SingleComposer = capi.Gui
                .CreateCompo("worldmap-modwp", dialogBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddDialogTitleBar(Lang.Get("Modify waypoint"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("Added By"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                    .AddStaticText(ownerName, CairoFont.WhiteSmallText(), rightColumn = rightColumn.FlatCopy())

                    .AddStaticText(Lang.Get("Name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                    .AddTextInput(rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(200), onNameChanged, CairoFont.TextInput(), "nameInput")

                    .AddStaticText(Lang.Get("Pinned"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                    .AddSwitch(onPinnedToggled, rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(200), "pinnedSwitch")

                    .AddStaticText(Lang.Get("Shared"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                    .AddSwitch(onSharedToggled, rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(200), "sharedSwitch")

                    .AddRichtext(Lang.Get("waypoint-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                    .AddColorListPicker(colors, onColorSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize, colorIconSize), 270, "colorpicker")

                    .AddStaticText(Lang.Get("Icon"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.WithFixedPosition(0, leftColumn.fixedY + leftColumn.fixedHeight).WithFixedWidth(100).BelowCopy(0, 0))
                    .AddIconListPicker(icons, onIconSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize+5, colorIconSize+5), 270, "iconpicker")

                    .AddSmallButton(Lang.Get("Cancel"), onCancel, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Delete"), onDelete, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.CenterFixed), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Save"), onSave, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, key: "saveButton")
                .EndChildElements()
                .Compose()
            ;

            var col = System.Drawing.Color.FromArgb(255, ColorUtil.ColorR(waypoint.Color), ColorUtil.ColorG(waypoint.Color), ColorUtil.ColorB(waypoint.Color));

            SingleComposer.ColorListPickerSetValue("colorpicker", colorIndex);
            SingleComposer.IconListPickerSetValue("iconpicker", iconIndex);

            SingleComposer.GetTextInput("nameInput").SetValue(waypoint.Title);
            SingleComposer.GetSwitch("pinnedSwitch").SetValue(waypoint.Pins.Contains(capi.World.Player.PlayerUID));
            SingleComposer.GetSwitch("sharedSwitch").SetValue(true);

        }

        private void onIconSelected(int index)
        {
            waypoint.Icon = icons[index];
        }

        private void onColorSelected(int index)
        {
            waypoint.Color = colors[index];
        }

        private void onPinnedToggled(bool t1)
        {

        }
        private void onSharedToggled(bool t1)
        {

        }

        private void onIconSelectionChanged(string code, bool selected)
        {

        }

        private bool onDelete()
        {
            capi.SendChatMessage(string.Format("/waypoint shared remove {0}.{1}", waypoint.PlayerName, waypoint.Index));
            TryClose();
            return true;
        }
        private bool onUnshare()
        {
            capi.SendChatMessage(string.Format("/waypoint unshare {0}.{1}", waypoint.PlayerName, waypoint.Index));
            TryClose();
            return true;
        }

        private bool onSave()
        {
            string name = SingleComposer.GetTextInput("nameInput").GetText();
            bool pinned = SingleComposer.GetSwitch("pinnedSwitch").On;
            bool shared = SingleComposer.GetSwitch("sharedSwitch").On;

            capi.SendChatMessage(string.Format("/waypoint shared modify {0}.{1} {2} {3} {4} {5}", waypoint.PlayerName, waypoint.Index, ColorUtil.Int2Hex(waypoint.Color), waypoint.Icon, pinned, name));
            if (!shared)
            {
                capi.SendChatMessage(string.Format("/waypoint unshare {0}.{1}", waypoint.PlayerName, waypoint.Index));
            }
            TryClose();
            return true;
        }

        private bool onCancel()
        {
            TryClose();
            return true;
        }

        private void onNameChanged(string t1)
        {
            SingleComposer.GetButton("saveButton").Enabled = (t1.Trim() != "");
        }


        public override bool CaptureAllInputs()
        {
            return IsOpened();
        }
        public override bool DisableMouseGrab => true;

        public override void OnMouseDown(MouseEvent args)
        {
            base.OnMouseDown(args);

            args.Handled = true;
        }

        public override void OnMouseUp(MouseEvent args)
        {
            base.OnMouseUp(args);
            args.Handled = true;
        }

        public override void OnMouseMove(MouseEvent args)
        {
            base.OnMouseMove(args);
            args.Handled = true;
        }

        public override void OnMouseWheel(MouseWheelEventArgs args)
        {
            base.OnMouseWheel(args);
            args.SetHandled(true);
        }

    }
}
