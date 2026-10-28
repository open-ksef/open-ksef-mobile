global using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Controls;

/// <summary>Minimal MAUI surface so linked app ViewModels compile on net8.0 without a device.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class QueryPropertyAttribute(string name, string queryId) : Attribute
{
    public string Name { get; } = name;
    public string QueryId { get; } = queryId;
}

public class Shell
{
    public static Shell Current { get; } = new();

    public Task GoToAsync(string state) => Task.CompletedTask;
}
