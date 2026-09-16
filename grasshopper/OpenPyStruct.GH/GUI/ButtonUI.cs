// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/ButtonUI.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace OpenPyStruct.GH.GUI;

public static class ButtonUI
{
    public static int ButtonHeight = 20;
    public static int PaddingTop = 2;

    public static void SetButtonText(this GH_BeautifulComponent component, string text)
    {
        component.ButtonText = text;
    }

    public static void SetButtonClickHandler(
        this GH_BeautifulComponent component,
        Action<GH_Canvas, GH_CanvasMouseEvent> handler
    )
    {
        component.OnButtonClick = handler;
    }


    public static void ModifyBounds(GH_ComponentUIAttributes attributes)
    {
        var bounds = GH_Convert.ToRectangle(attributes.Bounds);
        bounds.Height += ButtonHeight + PaddingTop;
        attributes.Bounds = bounds;
    }

    public static void Render(GH_ComponentUIAttributes attributes, Graphics graphics, bool mouseOver = false)
    {
        bool isDisabled = attributes.Component.OnButtonClick == null;
        var palette = attributes.Owner.Locked || isDisabled ? GH_Palette.Locked : GH_Palette.Black;
        var button = GH_Capsule.CreateTextCapsule(attributes.ButtonBounds, attributes.ButtonBounds,
            palette, attributes.Component.ButtonText, 2, 0);
        button.Render(graphics, attributes.Selected || (mouseOver && !isDisabled), attributes.Owner.Locked || isDisabled, false);
        button.Dispose();
    }

    public static GH_ObjectResponse RespondToMouseDown(
        GH_ComponentUIAttributes attributes, GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left
            && attributes.Component.OnButtonClick != null
            && attributes.ButtonBounds.Contains(GH_Convert.ToPoint(e.CanvasLocation)))
        {
            attributes.Component.OnButtonClick(sender, e);
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    public static RectangleF GetButtonBounds(GH_ComponentUIAttributes attributes)
    {
        var buttonBounds = attributes.Bounds;
        buttonBounds.Y = buttonBounds.Bottom - ButtonHeight - PaddingTop;
        buttonBounds.Height = ButtonHeight + PaddingTop;
        buttonBounds.Inflate(-PaddingTop, -PaddingTop);
        return buttonBounds;
    }
}
