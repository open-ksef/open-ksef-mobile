global using Microsoft.Maui.ApplicationModel;
global using Microsoft.Maui.ApplicationModel.DataTransfer;
global using Microsoft.Maui.Controls;
global using Microsoft.Maui.Devices;
global using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Controls
{
    /// <summary>Minimal MAUI surface so linked app sources compile on net8.0 without a device; static state is reset per test.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class QueryPropertyAttribute(string name, string queryId) : Attribute
    {
        public string Name { get; } = name;
        public string QueryId { get; } = queryId;
    }

    public class Page;

    public class Shell : Page
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

        public Task DisplayAlertAsync(string title, string message, string cancel)
        {
            LastAlertMessage = message;
            return Task.CompletedTask;
        }
    }

    public class Window
    {
        public Page? Page { get; set; }
    }

    public class Application
    {
        public static Application? Current { get; } = new();

        public List<Window> Windows { get; } = [new Window()];
    }
}

namespace OpenKSeF.Mobile
{
    public class AppShell : Shell;
}

namespace Microsoft.Maui.ApplicationModel.DataTransfer
{
    public class Clipboard
    {
        public static Clipboard Default { get; } = new();

        public string? Text { get; private set; }

        public Task SetTextAsync(string? text)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    public enum PermissionStatus { Unknown, Denied, Disabled, Granted, Restricted, Limited }

    public static class Permissions
    {
        public static PermissionStatus Status { get; set; } = PermissionStatus.Granted;
        public static PermissionStatus RequestResult { get; set; } = PermissionStatus.Granted;
        public static int RequestCount { get; private set; }

        public static void Reset()
        {
            Status = PermissionStatus.Granted;
            RequestResult = PermissionStatus.Granted;
            RequestCount = 0;
        }

        public static Task<PermissionStatus> CheckStatusAsync<T>() where T : BasePermission, new() => Task.FromResult(Status);

        public static Task<PermissionStatus> RequestAsync<T>() where T : BasePermission, new()
        {
            RequestCount++;
            return Task.FromResult(RequestResult);
        }

        public abstract class BasePermission;

        public class PostNotifications : BasePermission;
    }

    public class AppInfo
    {
        public static AppInfo Current { get; } = new();

        public string VersionString => "1.0";
        public string BuildString => "1";
    }

    public enum BrowserLaunchMode { SystemPreferred, External }

    public class Browser
    {
        public static Browser Default { get; } = new();

        public Task<bool> OpenAsync(string uri, BrowserLaunchMode mode) => Task.FromResult(true);
    }
}

namespace Microsoft.Maui.Devices
{
    public readonly record struct DevicePlatform(string Name)
    {
        public static DevicePlatform Android { get; } = new("Android");
        public static DevicePlatform iOS { get; } = new("iOS");
    }

    public static class DeviceInfo
    {
        public static DevicePlatform Platform { get; set; } = DevicePlatform.Android;
    }
}

namespace Microsoft.Maui.Storage
{
    public class Preferences
    {
        public static Preferences Default { get; } = new();

        private readonly Dictionary<string, object?> _values = [];

        public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out var value) ? (T)value! : defaultValue;

        public void Set<T>(string key, T value) => _values[key] = value;

        public void Remove(string key) => _values.Remove(key);

        public void Clear() => _values.Clear();
    }

    public static class FileSystem
    {
        public static string AppDataDirectory { get; } = Directory.CreateTempSubdirectory("openksef-tests-").FullName;
    }

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
