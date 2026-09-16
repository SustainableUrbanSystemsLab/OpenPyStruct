// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/ParamUI/ToggleUI.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System.Drawing;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace OpenPyStruct.GH.GUI;

public static class ToggleUI
{
    private static readonly int ToggleButtonRadius = 5;

    public static void Render(GH_ComponentUIAttributes attributes, Graphics graphics,
        RectangleF bounds, GH_ToggleParam toggleParam, bool mouseOver = false)
    {
        var palette = attributes.Owner.Locked ? GH_Palette.Locked : GH_Palette.Black;
        var capsule = GH_Capsule.CreateCapsule(bounds, palette, ToggleButtonRadius, 0);
        capsule.Render(graphics, attributes.Selected || mouseOver, attributes.Owner.Locked, false);
        capsule.Dispose();

        if (toggleParam.Toggle)
        {
            var rect = bounds;
            rect.Inflate(-2, -2);

            var color = attributes.Owner.Locked
                ? LabelUI.FadedLockedTextColor
                : (mouseOver ? LabelUI.TextColor : LabelUI.FadedTextColor);

            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, rect);
        }
    }

    public static GH_ObjectResponse RespondToMouseDown(GH_CanvasMouseEvent e, RectangleF bounds,
        GH_ToggleParam toggleParam)
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Left && bounds.Contains(e.CanvasLocation))
        {
            toggleParam.SetToggle(!toggleParam.Toggle);
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }

    public static bool IsToggleParam(this IGH_Param param)
    {
        return param is GH_ToggleParam toggleInput;
    }
}
