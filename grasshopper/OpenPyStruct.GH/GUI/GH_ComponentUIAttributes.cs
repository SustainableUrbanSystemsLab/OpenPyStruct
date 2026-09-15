// Adapted from Eddy3D (https://github.com/Eddy3D-Dev/Eddy3D), GUI/GH_ComponentUIAttributes.cs.
// Copyright (c) Eddy3D Authors. See GUI/README.md in this folder for provenance and licensing.

using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace OpenPyStruct.GH.GUI;

public class GH_ComponentUIAttributes : GH_ComponentAttributes
{
    public RectangleF ButtonBounds;
    private bool m_mouseOverButton;
    private int m_mouseOverParamIndex = -1;
    private bool _paramBoundsApplied;
    private int _layoutParamCount = -1;

    public GH_ComponentUIAttributes(GH_BeautifulComponent component) : base(component)
    {
    }

    public RectangleF InnerBounds
    {
        get => m_innerBounds;
        set => m_innerBounds = value;
    }

    public GH_BeautifulComponent Component => Owner as GH_BeautifulComponent;

    protected override void Layout()
    {
        base.Layout();
        // Widen BEFORE the widget zones are placed, so they lay out against the final body width;
        // LayoutOutputParams at the end of this method then re-seats the outputs on the new right
        // edge (widening Bounds alone would leave them floating mid-component).
        ApplyMinWidth();
        _paramBoundsApplied = Component.HasBeautifulParams;
        _layoutParamCount = Component.Params?.Input?.Count ?? 0;
        if (_paramBoundsApplied) ParamUI.ModifyBounds(this);

        // Mirror of the input widget column: extra width on the RIGHT, between the body and the
        // output capsules, for the component-level output dropdown.
        if (Component.HasOutputDropdown)
        {
            var bounds = Bounds;
            bounds.Width += ParamUI.TotalWidth;
            Bounds = bounds;
        }

        if (Component.HasButton)
        {
            ButtonUI.ModifyBounds(this);
            ButtonBounds = ButtonUI.GetButtonBounds(this);
        }

        if (Component.HasLabel) LabelUI.ModifyBounds(this);

        // Seat the output capsules past the widget strip, not on top of it: laying them against
        // InnerBounds would put them exactly where OutputDropdownBounds lives, and the capsule
        // attributes then swallow every click meant for the widget.
        var outputSeatBounds = InnerBounds;
        if (Component.HasOutputDropdown) outputSeatBounds.Width += ParamUI.TotalWidth;
        LayoutOutputParams(Owner, outputSeatBounds);
    }

    /// <summary>
    /// The output dropdown's widget square: just right of the body, vertically centered — the
    /// mirror position of an input widget. Empty when the component has none.
    /// </summary>
    public RectangleF OutputDropdownBounds =>
        Component.HasOutputDropdown
            ? new RectangleF(
                InnerBounds.Right + ParamUI.PaddingRight,
                InnerBounds.Top + InnerBounds.Height / 2f - ParamUI.Width / 2f,
                ParamUI.Width, ParamUI.Width)
            : RectangleF.Empty;

    /// <summary>
    /// Grows the body to fit <see cref="GH_BeautifulComponent.MinWidthText"/>, so a Message wider
    /// than the component does not wrap onto a second line.
    /// </summary>
    private void ApplyMinWidth()
    {
        var mustFit = Component?.MinWidthText;
        if (string.IsNullOrEmpty(mustFit)) return;

        var needed = MinWidthComponentAttributes.RequiredWidth(mustFit);
        var extra = needed - Bounds.Width;
        if (extra <= 0) return;

        var bounds = Bounds;
        bounds.Width += extra;
        Bounds = bounds;
        m_innerBounds.Width += extra;
    }

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        var currentParamCount = Component.Params?.Input?.Count ?? 0;
        if (Component.HasBeautifulParams && (!_paramBoundsApplied || currentParamCount != _layoutParamCount))
        {
            ExpireLayout();
            PerformLayout();
        }

        base.Render(canvas, graphics, channel);

        if (channel != GH_CanvasChannel.Objects)
            return;
        if (CentralSettings.CanvasZuiZoomLevel >= GH_Canvas.ZoomFadeLow) return;

        if (Component.HasBeautifulParams) ParamUI.Render(this, graphics, m_mouseOverParamIndex);

        if (Component.HasOutputDropdown)
            DropdownUI.Render(this, graphics, OutputDropdownBounds, Component.OutputDropdown,
                _mouseOverOutputDropdown);

        if (Component.HasButton) ButtonUI.Render(this, graphics, m_mouseOverButton);

        if (Component.HasLabel) LabelUI.Render(this, graphics);
    }

    private bool _mouseOverOutputDropdown;

    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        bool needsRedraw = false;

        if (Component.HasButton)
        {
            bool isOver = !Owner.Locked && ButtonBounds.Contains(e.CanvasLocation);
            if (isOver != m_mouseOverButton)
            {
                m_mouseOverButton = isOver;
                needsRedraw = true;
            }
        }

        var inputParams = Component.HasBeautifulParams ? Component.Params?.Input : null;
        if (inputParams != null)
        {
            int hoveredIndex = -1;
            if (!Owner.Locked)
            {
                for (var i = 0; i < inputParams.Count; i++)
                {
                    var rect = ParamUI.GetParamUIRectangle(this, i);
                    rect.Inflate(2f, 2f);
                    if (rect.Contains(e.CanvasLocation))
                    {
                        hoveredIndex = i;
                        break;
                    }
                }
            }

            if (hoveredIndex != m_mouseOverParamIndex)
            {
                m_mouseOverParamIndex = hoveredIndex;
                needsRedraw = true;
            }
        }

        if (Component.HasOutputDropdown)
        {
            var rect = OutputDropdownBounds;
            rect.Inflate(2f, 2f);
            var over = !Owner.Locked && rect.Contains(e.CanvasLocation);
            if (over != _mouseOverOutputDropdown)
            {
                _mouseOverOutputDropdown = over;
                needsRedraw = true;
            }
        }

        if (needsRedraw)
        {
            sender.Invalidate();
            return GH_ObjectResponse.Handled;
        }

        if (m_mouseOverButton || m_mouseOverParamIndex != -1 || _mouseOverOutputDropdown)
        {
            try { ((dynamic)Grasshopper.Instances.CursorServer).AttachCursor(sender, "GH_Hand"); } catch (System.Exception) { /* Ignore fallback errors */ }
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseMove(sender, e);
    }

    public override bool IsTooltipRegion(PointF canvasLocation)
    {
        if (Component.HasButton && ButtonBounds.Contains(canvasLocation)) return true;
        if (Component.HasOutputDropdown && OutputDropdownBounds.Contains(canvasLocation)) return true;

        var tooltipParams = Component.HasBeautifulParams ? Component.Params?.Input : null;
        if (tooltipParams != null)
        {
            for (var i = 0; i < tooltipParams.Count; i++)
            {
                var rect = ParamUI.GetParamUIRectangle(this, i);
                rect.Inflate(2f, 2f);
                if (rect.Contains(canvasLocation)) return true;
            }
        }

        return base.IsTooltipRegion(canvasLocation);
    }

    public override void SetupTooltip(PointF canvasLocation, GH_TooltipDisplayEventArgs e)
    {
        if (Component.HasOutputDropdown && OutputDropdownBounds.Contains(canvasLocation))
        {
            var dropdown = Component.OutputDropdown;
            e.Title = dropdown.Name;
            e.Text = dropdown.Description;
            var selected = dropdown.SelectedLabels.ToList();
            e.Text += selected.Count > 0
                ? "\n\nSelected: " + string.Join(", ", selected.Take(5))
                  + (selected.Count > 5 ? $" (+{selected.Count - 5} more)" : "")
                : "\n\nSelected: None";
            if (!Owner.Locked) e.Text += " (Click to change)";
            e.Icon = Owner.Icon_24x24;
            return;
        }

        if (Component.HasButton && ButtonBounds.Contains(canvasLocation))
        {
            e.Title = Component.ButtonText;
            e.Text = Component.ButtonToolTip ?? "";
            if (Owner.Locked && Component.OnButtonClick != null)
            {
                if (!string.IsNullOrEmpty(e.Text)) e.Text += "\n\n";
                e.Text += "(Disabled)";
            }
            e.Icon = Owner.Icon_24x24;
        }
        else if (Component.HasBeautifulParams && Component.Params?.Input is { } setupParams)
        {
            for (var i = 0; i < setupParams.Count; i++)
            {
                var rect = ParamUI.GetParamUIRectangle(this, i);
                rect.Inflate(2f, 2f);
                if (rect.Contains(canvasLocation))
                {
                    var param = setupParams[i];
                    e.Title = param.Name + (param.Optional ? " (Optional)" : " (Required)");
                    e.Text = param.Description;

                    if (param is GH_ToggleParam toggle)
                    {
                        e.Text += "\n\nStatus: " + (toggle.Toggle ? "🟢 ON" : "⚪ OFF");
                        if (!Owner.Locked) e.Text += " (Click to toggle)";
                    }
                    else if (param is GH_DropdownParam dropdown)
                    {
                        var selected = dropdown.SelectedLabels.ToList();
                        if (selected.Count > 0)
                        {
                            var displayList = selected.Take(5).ToList();
                            var text = string.Join(", ", displayList);
                            if (selected.Count > 5)
                            {
                                text += $" (+{selected.Count - 5} more)";
                            }
                            e.Text += "\n\nSelected: " + text;
                            if (!Owner.Locked) e.Text += " (Click to change)";
                        }
                        else
                        {
                            e.Text += "\n\nSelected: None";
                            if (!Owner.Locked) e.Text += " (Click to select)";
                        }
                    }

                    if (Owner.Locked) e.Text += "\n\n(Disabled)";

                    e.Icon = Owner.Icon_24x24;
                    return;
                }
            }
            base.SetupTooltip(canvasLocation, e);
        }
        else
        {
            base.SetupTooltip(canvasLocation, e);
        }
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (CentralSettings.CanvasZuiZoomLevel >= GH_Canvas.ZoomFadeLow) return GH_ObjectResponse.Ignore;

        if (Owner.Locked) return base.RespondToMouseDown(sender, e);

        if (Component.HasBeautifulParams)
        {
            var response = ParamUI.RespondToMouseDown(this, e);
            if (response != GH_ObjectResponse.Ignore) return response;
        }

        if (Component.HasOutputDropdown)
        {
            var rect = OutputDropdownBounds;
            rect.Inflate(2f, 2f);
            var response = DropdownUI.RespondToMouseDown(e, rect, Component.OutputDropdown);
            if (response != GH_ObjectResponse.Ignore) return response;
        }

        if (Component.HasButton)
        {
            var response = ButtonUI.RespondToMouseDown(this, sender, e);
            if (response != GH_ObjectResponse.Ignore) return response;
        }

        return base.RespondToMouseDown(sender, e);
    }
}
