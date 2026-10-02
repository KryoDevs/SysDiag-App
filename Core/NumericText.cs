using System.Globalization;
using System.Text.RegularExpressions;

namespace SysDiag.Core;

/// <summary>Los porcentajes de los recolectores no usan separadores de miles: admitir coma o punto decimal.</summary>
public static class NumericText
{
    public static bool TryRead(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = Regex.Match(text, @"(?<![\d.,])-?\d+(?:[.,]\d+)?");
        return match.Success && double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}
