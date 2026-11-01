using System.Collections.Concurrent;
using System.Reflection;

namespace MyGarage.Database.Common;

// Loads the embedded .sql files under Scripts/ once and caches their text.
public sealed class SqlScriptStore
{
    private const string ResourcePrefix = "MyGarage.Database.Scripts.";
    private const string ResourceSuffix = ".sql";

    private static readonly Assembly ScriptAssembly = typeof(SqlScriptStore).Assembly;

    private readonly ConcurrentDictionary<string, string> cache = new(StringComparer.Ordinal);

    public SqlScriptStore() => DapperSetup.Ensure();

    // name is "<Feature>/<Script>", for example "Vehicles/GetById".
    public string Get(string name) => cache.GetOrAdd(name, Load);

    // Script identifiers (file names without extension) in a feature folder, in ordinal order.
    public IReadOnlyList<string> List(string folder)
    {
        var prefix = ResourcePrefix + folder + ".";
        return ScriptAssembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(prefix, StringComparison.Ordinal) && resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(resource => resource[prefix.Length..^ResourceSuffix.Length])
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    internal static IReadOnlyList<string> AllResourceNames() =>
        ScriptAssembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToList();

    private static string Load(string name)
    {
        var resource = ResourcePrefix + name.Replace('/', '.') + ResourceSuffix;
        using var stream = ScriptAssembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"SQL script '{name}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
