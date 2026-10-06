using System;

internal static class Rule09FamilyIgnoreEmulation
{
    private const string IgnoredFamilyName = "Beredskapspanel sikkerhetsventilasjon";

    internal static bool ShouldIgnore(string? familyName)
    {
        return string.Equals(familyName, IgnoredFamilyName, StringComparison.OrdinalIgnoreCase);
    }
}