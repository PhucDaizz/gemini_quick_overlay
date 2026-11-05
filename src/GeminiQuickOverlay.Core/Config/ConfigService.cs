using System.Text.Encodings.Web;
using System.Text.Json;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Models;
using GeminiQuickOverlay.Core.Paths;

namespace GeminiQuickOverlay.Core.Config;

public sealed class ConfigService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer? _debounce;
    private AppConfig _config;
    private DateTime _lastWriteUtc;
    private bool _disposed;
    private DateTime _suppressWatcherUntilUtc;

    public ConfigService(string? configPath = null)
    {
        _path = configPath ?? AppPaths.ConfigPath;
        _config = Load();
        _lastWriteUtc = GetLastWriteUtc();

        if (configPath is null)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                _watcher = new FileSystemWatcher(directory ?? AppPaths.BaseDirectory, System.IO.Path.GetFileName(_path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileChanged;
                _debounce = new System.Threading.Timer(_ => ReloadFromWatcher(), null, Timeout.Infinite, Timeout.Infinite);
            }
            catch (Exception exception)
            {
                AppLog.Warn($"Không theo dõi được config: {exception.Message}");
            }
        }
    }

    public event EventHandler? SettingsChanged;

    public string Path => _path;

    public AppConfig Current
    {
        get
        {
            ReloadIfChanged();
            lock (_gate)
            {
                return _config;
            }
        }
    }

    public AppConfig GetSnapshot()
    {
        ReloadIfChanged();
        lock (_gate)
        {
            return _config.Clone();
        }
    }

    public void Save(AppConfig config)
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            var temporaryPath = $"{_path}.tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _path, true);
            _config = config.Clone();
            _lastWriteUtc = GetLastWriteUtc();
            _suppressWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(750);
        }

        AppLog.Info("Đã lưu config.");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadIfChanged()
    {
        var currentWriteUtc = GetLastWriteUtc();
        if (currentWriteUtc == _lastWriteUtc)
        {
            return;
        }

        lock (_gate)
        {
            currentWriteUtc = GetLastWriteUtc();
            if (currentWriteUtc == _lastWriteUtc)
            {
                return;
            }
            _config = Load();
            _lastWriteUtc = currentWriteUtc;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    private AppConfig Load()
    {
        var config = new AppConfig();
        if (!File.Exists(_path))
        {
            AppLog.Warn($"Không tìm thấy config tại {_path}, dùng mặc định.");
            return config;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (loaded is null)
            {
                return config;
            }
            loaded.Selectors ??= new Selectors();
            AppLog.Info($"Đã load config từ: {_path}");
            return loaded;
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Lỗi đọc config");
            return config;
        }
    }

    private DateTime GetLastWriteUtc()
    {
        try
        {
            return File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow < _suppressWatcherUntilUtc)
        {
            return;
        }
        _debounce?.Change(300, Timeout.Infinite);
    }

    private void ReloadFromWatcher()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            var changed = false;
            lock (_gate)
            {
                var currentWriteUtc = GetLastWriteUtc();
                if (currentWriteUtc == _lastWriteUtc)
                {
                    return;
                }
                _config = Load();
                _lastWriteUtc = currentWriteUtc;
                changed = true;
            }
            if (changed)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Lỗi reload config");
        }
    }
}
