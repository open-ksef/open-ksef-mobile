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

    public interface IQueryAttributable
    {
        void ApplyQueryAttributes(IDictionary<string, object> query);
    }

    public class ImageSource
    {
        public Func<Stream>? Stream { get; private init; }

        public static ImageSource FromStream(Func<Stream> stream) => new() { Stream = stream };
    }

    public class Page;

    public class Shell : Page
    {
        public static Shell Current { get; } = new();

        public bool AlertAnswer { get; set; } = true;
        public string? LastAlertTitle { get; private set; }
        public string? LastAlertMessage { get; private set; }
        public List<(string Title, string Message)> Alerts { get; } = [];
        public string? LastRoute { get; private set; }
        public Exception? GoToFailure { get; set; }
        public Func<Task>? WhileConfirming { get; set; }
        public Action? OnAlert { get; set; }

        public void Reset()
        {
            AlertAnswer = true;
            LastAlertTitle = null;
            LastAlertMessage = null;
            LastRoute = null;
            GoToFailure = null;
            WhileConfirming = null;
            OnAlert = null;
            Alerts.Clear();
        }

        public Task GoToAsync(string state)
        {
            if (GoToFailure is not null)
                return Task.FromException(GoToFailure);
            LastRoute = state;
            return Task.CompletedTask;
        }

        public async Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
        {
            LastAlertMessage = message;
            Alerts.Add((title, message));
            if (WhileConfirming is not null)
                await WhileConfirming();
            return AlertAnswer;
        }

        public Task DisplayAlertAsync(string title, string message, string cancel)
        {
            LastAlertTitle = title;
            LastAlertMessage = message;
            Alerts.Add((title, message));
            OnAlert?.Invoke();
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

        public AppTheme UserAppTheme { get; set; }
    }
}

namespace OpenKSeF.Mobile
{
    public class AppShell : Shell;
}

namespace Microsoft.Maui.ApplicationModel.DataTransfer
{
    public class ShareFile(string fullPath, string? contentType = null)
    {
        public string FullPath { get; } = fullPath;
        public string? ContentType { get; } = contentType;
    }

    public class ShareFileRequest
    {
        public string? Title { get; set; }
        public ShareFile? File { get; set; }
    }

    public class Share
    {
        public static Share Default { get; } = new();

        public List<ShareFileRequest> Requests { get; } = [];

        public Task RequestAsync(ShareFileRequest request)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    public class Clipboard
    {
        public static Clipboard Default { get; } = new();

        public string? Text { get; private set; }

        public Exception? Failure { get; set; }

        public Task SetTextAsync(string? text)
        {
            if (Failure is not null)
                return Task.FromException(Failure);

            Text = text;
            return Task.CompletedTask;
        }
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    public enum AppTheme { Unspecified, Light, Dark }

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

        public string? LastUri { get; set; }

        public Task<bool> OpenAsync(string uri, BrowserLaunchMode mode)
        {
            LastUri = uri;
            return Task.FromResult(true);
        }
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

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public void Set<T>(string key, T value) => _values[key] = value;

        public void Remove(string key) => _values.Remove(key);

        public void Clear() => _values.Clear();
    }

    public class SecureStorage
    {
        public static SecureStorage Default { get; } = new();

        private readonly Dictionary<string, string> _values = [];

        public bool ThrowOnRead { get; set; }

        public int WritesLeft { get; set; } = int.MaxValue;

        public int Count => _values.Count;

        public void Reset()
        {
            _values.Clear();
            ThrowOnRead = false;
            WritesLeft = int.MaxValue;
        }

        public Task<string?> GetAsync(string key) => ThrowOnRead
            ? throw new InvalidOperationException("keystore")
            : Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value)
        {
            if (WritesLeft-- <= 0)
                throw new InvalidOperationException("keystore");

            _values[key] = value;
            return Task.CompletedTask;
        }

        public bool Remove(string key) => _values.Remove(key);
    }

    public static class FileSystem
    {
        public static string AppDataDirectory { get; } = Directory.CreateTempSubdirectory("openksef-tests-").FullName;
        public static string CacheDirectory { get; } = Directory.CreateTempSubdirectory("openksef-tests-cache-").FullName;
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
