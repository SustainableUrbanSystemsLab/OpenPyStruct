using System.Linq;
using OpenPyStruct.GH.GUI;

namespace OpenPyStruct.GH.CMP;

/// <summary>Reading an inline dropdown by input index, with the option list's default as fallback.</summary>
internal static class Dropdowns
{
    public static string Selected(this GH_BeautifulComponent c, int index, string fallback)
    {
        if (c.Params.Input.Count > index && c.Params.Input[index] is GH_DropdownParam dd)
            return dd.SelectedValues.FirstOrDefault() ?? fallback;
        return fallback;
    }
}
