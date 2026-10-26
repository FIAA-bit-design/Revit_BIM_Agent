using System;
using System.Globalization;

namespace CW.Assistant.Generated;

internal static class Rule14RoomValueValidator
{
    private const string MissingMarker = "--";

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