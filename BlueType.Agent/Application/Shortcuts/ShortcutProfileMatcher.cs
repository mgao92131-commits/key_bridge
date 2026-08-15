namespace BlueType.Agent.Application.Shortcuts;

internal sealed class ShortcutProfileMatcher
{
    public ShortcutProfileDefinition? Match(
        IReadOnlyList<ShortcutProfileDefinition> profiles,
        string? processName)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            var appProfile = profiles.FirstOrDefault(profile =>
                profile.Match.WindowsProcesses.Any(candidate => ProcessNamesEqual(candidate, processName)));
            if (appProfile is not null)
            {
                return appProfile;
            }
        }

        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, "default", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProcessNamesEqual(string configured, string actual)
    {
        static string Normalize(string value) =>
            value.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value.Trim()[..^4]
                : value.Trim();

        return string.Equals(Normalize(configured), Normalize(actual), StringComparison.OrdinalIgnoreCase);
    }
}
