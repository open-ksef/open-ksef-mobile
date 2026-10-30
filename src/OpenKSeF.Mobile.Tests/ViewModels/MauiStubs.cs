global using Microsoft.Maui.Controls;
global using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Controls
{
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

        public bool AlertAnswer { get; set; } = true;
        public string? LastAlertMessage { get; private set; }
        public string? LastRoute { get; private set; }

        public void Reset()
        {
            AlertAnswer = true;
            LastAlertMessage = null;
            LastRoute = null;
        }

        public Task GoToAsync(string state)
        {
            LastRoute = state;
            return Task.CompletedTask;
        }

        public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
        {
            LastAlertMessage = message;
            return Task.FromResult(AlertAnswer);
        }
    }
}

namespace Microsoft.Maui.Storage
{
    public class FileResult
    {
        public Task<Stream> OpenReadAsync() => Task.FromResult<Stream>(new MemoryStream());
    }

    public class FilePickerFileType
    {
        public static FilePickerFileType Images { get; } = new();
    }

    public class PickOptions
    {
        public string? PickerTitle { get; set; }
        public FilePickerFileType? FileTypes { get; set; }
    }

    public class FilePicker
    {
        public static FilePicker Default { get; } = new();

        public Task<FileResult?> PickAsync(PickOptions? options = null) => Task.FromResult<FileResult?>(null);
    }
}
