using System.Reflection;

namespace AudioHQ.Core;

/// <summary>
/// Exposes the canonical app version (set once in Directory.Build.props) to
/// every front end (WPF title bar, CLI banner). Reads the informational
/// version of this assembly; all AudioHQ assemblies share the same number.
/// </summary>
public static class AppVersion
{
    /// <summary>Display string, e.g. "0.1.0" (any "+commit" suffix stripped).</summary>
    public static string Display { get; } = ReadDisplay();

    private static string ReadDisplay()
    {
        string? info = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
            return typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "dev";
        int plus = info.IndexOf('+');
        return plus > 0 ? info[..plus] : info;
    }
}
