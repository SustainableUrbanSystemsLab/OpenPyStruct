// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/LabelUI.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace OpenPyStruct.GH.GUI;

public static class LabelUI
{
    public static int HeightPerLine = 15;
    public static Color BackgroundColor => Color.FromArgb(GH_Canvas.ZoomFadeLow, 35, 35, 35);
    public static Color TextColor => Color.FromArgb(248, 248, 248);
    public static Color FadedTextColor => Color.FromArgb(GH_Canvas.ZoomFadeLow, TextColor);
    public static Color LockedTextColor => Color.FromArgb(155, 155, 155);
    public static Color FadedLockedTextColor => Color.FromArgb(GH_Canvas.ZoomFadeLow, LockedTextColor);

    public static void SetLabel(this GH_BeautifulComponent component, string label)
    {
        component.Labels = new[] { label };
        component.Attributes?.ExpireLayout();
    }

    public static void SetLabels(this GH_BeautifulComponent component, IEnumerable<string> labels)
    {
        component.Labels = labels.ToArray();
        component.Attributes?.ExpireLayout();
    }

    public static void ModifyBounds(GH_ComponentUIAttributes attributes)
    {
        if (attributes.Component.HasLabel)
        {
            var bounds = attributes.Bounds;
            bounds.Height += HeightPerLine * attributes.Component.Labels.Length;
            attributes.Bounds = bounds;
        }
    }

    public static void Render(GH_ComponentUIAttributes attributes, Graphics graphics)
    {
        var bounds = attributes.Bounds;
        var labels = attributes.Component.Labels;
        if (labels == null || labels.Length == 0) return;

        var smallFont = GH_FontServer.Small;
        var fontSize = (float)Math.Round(116M / GH_FontServer.Standard.Height);
        var standardFontAdjust = GH_FontServer.NewFont(GH_FontServer.Standard, fontSize);

        var maxLabel = labels.OrderByDescending(l => l.Length).First();
        var maxLabelWidth = GH_FontServer.StringWidth(maxLabel, standardFontAdjust);
        var labelBoxWidth = (int)bounds.Width - 6;

        if (labelBoxWidth < maxLabelWidth)
        {
            standardFontAdjust =
                GH_FontServer.NewFont(GH_FontServer.Standard, fontSize * labelBoxWidth / maxLabelWidth);
            maxLabelWidth = GH_FontServer.StringWidth(labels, standardFontAdjust);
        }

        labelBoxWidth = Math.Max(maxLabelWidth + 4, labelBoxWidth);

        var lines = labels.Length;
        var height = HeightPerLine * lines;
        var yTop = bounds.Bottom - height;

        var drawingRectangle = new Rectangle(
            (int)(bounds.X + (bounds.Width / 2 - labelBoxWidth / 2)),
            (int)yTop,
            labelBoxWidth,
            height);
        var BackgroundBrush = new SolidBrush(BackgroundColor);
        var textColorToUse = attributes.Owner.Locked ? FadedLockedTextColor : FadedTextColor;
        var TextBrush = new SolidBrush(textColorToUse);

        var state = graphics.Save();
        graphics.SetClip(drawingRectangle, CombineMode.Union);
        graphics.FillRectangle(BackgroundBrush, drawingRectangle);
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            var labelWidth = GH_FontServer.StringWidth(label, standardFontAdjust);
            graphics.DrawString(
                label,
                standardFontAdjust,
                TextBrush,
                new PointF(bounds.Left + bounds.Width / 2 - labelWidth / 2, yTop + HeightPerLine * i)
            );
        }
        graphics.Restore(state);

        BackgroundBrush.Dispose();
        TextBrush.Dispose();
        standardFontAdjust.Dispose();
    }
}
