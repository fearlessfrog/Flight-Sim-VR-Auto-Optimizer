using Microsoft.Win32;

namespace SimVROptimizer.Core;

public sealed record ServiceDependencyInfo(
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> DependentServices);

/// <summary>Reads the service dependency graph without changing service state.</summary>
public static class ServiceDependencyInspector
{
    private const string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";

    public static IReadOnlyDictionary<string, ServiceDependencyInfo> Read(IEnumerable<string> serviceNames)
    {
        var requested = serviceNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = requested.ToDictionary(
            name => name,
            _ => new ServiceDependencyInfo([], []),
            StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows() || requested.Length == 0) return result;

        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(ServicesRegistryPath);
            if (services is null) return result;
            var dependencyMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var installedName in services.GetSubKeyNames())
            {
                try
                {
                    using var service = services.OpenSubKey(installedName);
                    var dependencies = ReadMultiString(service?.GetValue("DependOnService"));
                    var groups = ReadMultiString(service?.GetValue("DependOnGroup"))
                        .Select(group => "+" + group.TrimStart('+'));
                    dependencyMap[installedName] = dependencies.Concat(groups)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }

            foreach (var serviceName in requested)
            {
                var dependencies = dependencyMap.GetValueOrDefault(serviceName) ?? [];
                var dependents = FindDependents(serviceName, dependencyMap);
                result[serviceName] = new(dependencies, dependents);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return result;
    }

    public static IReadOnlyList<string> FindDependents(
        string serviceName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencyMap) =>
        dependencyMap
            .Where(pair => pair.Value.Any(dependency =>
                dependency.TrimStart('+').Equals(serviceName, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<string> ReadMultiString(object? value) => value switch
    {
        string[] values => values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
        string text when !string.IsNullOrWhiteSpace(text) => text
            .Split(['\0', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        _ => []
    };
}
