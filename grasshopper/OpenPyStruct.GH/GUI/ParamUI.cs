// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/ParamUI/ParamUI.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System.Drawing;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;

namespace OpenPyStruct.GH.GUI;

public static class ParamUI
{
    public static int Width = 10;
    public static int PaddingRight = 8;
    public static int TotalWidth = Width + PaddingRight;
    public static int VerticalGutter = 2;

    public static void ModifyBounds(GH_ComponentUIAttributes attributes)
    {
        var bounds = attributes.Bounds;
        bounds.Width += TotalWidth;
        attributes.Bounds = bounds;

        var innerBounds = attributes.InnerBounds;
        innerBounds.X += TotalWidth;
        attributes.InnerBounds = innerBounds;
    }

    public static RectangleF GetParamUIRectangle(this GH_ComponentUIAttributes attributes, int index)
    {
        var bounds = attributes.Bounds;
        if (attributes.Component.HasButton)
            bounds.Height -= ButtonUI.ButtonHeight + ButtonUI.PaddingTop;

        var inputCount = attributes.Owner.Params.Input.Count;
        if (inputCount == 0) return RectangleF.Empty;

        var yOffsetPerParam = (bounds.Height - 2 * VerticalGutter) / inputCount;
        var yOffset = (float)((index + 0.5) * yOffsetPerParam);
        var top = bounds.Top + VerticalGutter + yOffset;
        var topLeft = new PointF(
            attributes.InnerBounds.Left - PaddingRight - Width,
            top - Width / 2f
        );
        var size = new SizeF(Width, Width);
        return new RectangleF(topLeft, size);
    }


    public static void Render(GH_ComponentUIAttributes attributes, Graphics graphics, int mouseOverParamIndex = -1)
    {
        var inputParams = attributes.Component.Params.Input;
        for (var i = 0; i < inputParams.Count; i++)
            if (inputParams[i].IsDropdownParam())
                DropdownUI.Render(
                    attributes,
                    graphics,
                    GetParamUIRectangle(attributes, i),
                    inputParams[i] as GH_DropdownParam,
                    i == mouseOverParamIndex
                );
            else if (inputParams[i].IsToggleParam())
                ToggleUI.Render(
                    attributes,
                    graphics,
                    GetParamUIRectangle(attributes, i),
                    inputParams[i] as GH_ToggleParam,
                    i == mouseOverParamIndex
                );
    }

    public static GH_ObjectResponse RespondToMouseDown(GH_ComponentUIAttributes attributes, GH_CanvasMouseEvent e)
    {
        if (e.Button != MouseButtons.Left) return GH_ObjectResponse.Ignore;

        var inputParams = attributes.Component.Params.Input;
        for (var i = 0; i < inputParams.Count; i++)
        {
            var rect = GetParamUIRectangle(attributes, i);
            rect.Inflate(2f, 2f);

            if (inputParams[i].IsDropdownParam())
            {
                var dropdownResponse = DropdownUI.RespondToMouseDown(e,
                    rect, inputParams[i] as GH_DropdownParam);
                if (dropdownResponse != GH_ObjectResponse.Ignore)
                    return dropdownResponse;
            }
            else if (inputParams[i].IsToggleParam())
            {
                var toggleResponse = ToggleUI.RespondToMouseDown(e,
                    rect, inputParams[i] as GH_ToggleParam);
                if (toggleResponse != GH_ObjectResponse.Ignore)
                    return toggleResponse;
            }
        }

        return GH_ObjectResponse.Ignore;
    }
}
