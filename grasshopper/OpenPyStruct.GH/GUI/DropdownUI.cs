// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/ParamUI/DropdownUI.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System.Drawing;
using System.Drawing.Drawing2D;

using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace OpenPyStruct.GH.GUI;

public static class DropdownUI
{
    private static readonly float DropdownTriangleWidth = 6.5f;
    private static readonly float DropdownTriangleHeight = 6.5f;

    /// <summary>
    /// Whether the inline widget column draws this parameter.
    ///
    /// <para>A dropdown with a BROWSER counts even when it has no options: a library too large to
    /// enumerate (the climate catalog's 60,868 stations) starts empty, and without this the
    /// component would render no widget at all — the picker would exist only in the parameter's
    /// right-click menu, which is where nobody looks.</para>
    /// </summary>
    public static bool IsDropdownParam(this IGH_Param param)
    {
        return param is GH_DropdownParam dropdownInput
               && (dropdownInput.HasOptions || dropdownInput.Browse != null);
    }

    public static void HookToLabel(
        this GH_BeautifulComponent component,
        GH_DropdownParam param
    )
    {
        param.HandleValueSelected = selected => component.SetLabels(param.SelectedLabels);
        component.SetLabels(param.SelectedLabels);
    }

    public static void Render(
        GH_ComponentUIAttributes attributes,
        Graphics graphics,
        RectangleF bounds,
        GH_DropdownParam param,
        bool mouseOver = false
    )
    {
        var palette = attributes.Owner.Locked ? GH_Palette.Locked : GH_Palette.Black;
        var capsule = GH_Capsule.CreateCapsule(bounds, palette, ParamUI.Width / 2, 0);
        capsule.Render(graphics, attributes.Selected || mouseOver, attributes.Owner.Locked, false);
        capsule.Dispose();

        var color = attributes.Owner.Locked
            ? LabelUI.FadedLockedTextColor
            : (mouseOver ? LabelUI.TextColor : LabelUI.FadedTextColor);

        using var brush = new SolidBrush(color);
        graphics.FillPolygon(
            brush,
            Draw.Triangle(bounds.GetCenter(), DropdownTriangleWidth, DropdownTriangleHeight),
            FillMode.Winding);
    }

    public static GH_ObjectResponse RespondToMouseDown(
        GH_CanvasMouseEvent e,
        RectangleF bounds,
        GH_DropdownParam param)
    {
        if (e.Button == MouseButtons.Left && bounds.Contains(e.CanvasLocation))
        {
            // CreateDropdownMenu already pairs the menu with CanvasMenu.DisposeOnClose.
            var menu = param.CreateDropdownMenu();
            menu.Show(Cursor.Position);
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Ignore;
    }
}
