// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/Draw.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System.Drawing;

namespace OpenPyStruct.GH.GUI;

public static class Draw
{
    public static PointF GetCenter(this RectangleF region)
    {
        return new PointF(
            (float)(region.Left + region.Width / 2.0),
            (float)(region.Top + region.Height / 2.0)
        );
    }

    public static PointF[] Triangle(PointF center, float width, float height)
    {
        var xOffset = width / 2f;
        var yUpOffset = height * (2.0f / 5.0f);
        var yDownOffset = height * (3.0f / 5.0f);
        return new PointF[3]
        {
            new(center.X - xOffset, center.Y - yUpOffset),
            new(center.X + xOffset, center.Y - yUpOffset),
            new(center.X, center.Y + yDownOffset)
        };
    }
}
