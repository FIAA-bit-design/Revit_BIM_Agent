using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CW.Assistant.Generated;

internal static class Rule14RoomValueValidator
{
    private const string MissingMarker = "--";
    private static readonly Regex MillimeterSizePattern = new(
        @"(?<![\d.,])(\d+(?:[.,]\d+)?)\s*mm(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool RequiresConduitPlaceholder(string? enterprise, string? sizeValue) =>
        !string.Equals(enterprise, "K5B", StringComparison.Ordinal)
        || !HasSizeAtLeast50Mm(sizeValue);

    private static bool HasSizeAtLeast50Mm(string? sizeValue)
    {
        if (string.IsNullOrWhiteSpace(sizeValue)) return false;
        foreach (Match match in MillimeterSizePattern.Matches(sizeValue))
        {
            string value = match.Groups[1].Value.Replace(',', '.');
            if (decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal size)
                && size >= 50m) return true;
        }
        return false;
    }

    internal static string? FindParameterIssue(int parameterCount, string? storageType, string? value, bool requiresPlaceholder)
    {
        if (parameterCount == 0) return "parameter mangler";
        if (parameterCount > 1) return "dupliserte instansparametere (" + parameterCount.ToString(CultureInfo.InvariantCulture) + ")";
        if (!string.Equals(storageType, "String", StringComparison.Ordinal))
            return "feil lagringstype " + (storageType ?? "ukjent");

        if (requiresPlaceholder)
            return string.Equals(value, MissingMarker, StringComparison.Ordinal)
                ? null
                : "krever nøyaktig '--'";

        if (string.IsNullOrWhiteSpace(value)) return "blank verdi";
        return string.Equals(value.Trim(), MissingMarker, StringComparison.Ordinal)
            ? "verdien '--' er ikke gyldig"
            : null;
    }
}