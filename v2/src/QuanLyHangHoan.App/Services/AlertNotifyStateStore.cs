using System.IO;
using System.Text.Json;

namespace QuanLyHangHoan.App.Services;

public interface IAlertNotifyStateStore
{
    int Count { get; }
    bool HasNotified(string key);
    void MarkNotified(IEnumerable<string> keys);
}

/// <summary>
/// Lưu danh sách khóa cảnh báo đã gửi thông báo (tối đa 5.000 khóa, ghi nguyên tử cạnh settings.json).
/// </summary>
public sealed class FileAlertNotifyStateStore : IAlertNotifyStateStore
{
    public const int MaxKeys = 5000;

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<string> _orderedKeys = new();
    private readonly HashSet<string> _keySet = new(StringComparer.Ordinal);
    private bool _loaded;

    public FileAlertNotifyStateStore(string path)
    {
        _path = path;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                EnsureLoadedLocked();
                return _orderedKeys.Count;
            }
        }
    }

    public bool HasNotified(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_gate)
        {
            EnsureLoadedLocked();
            return _keySet.Contains(key);
        }
    }

    public void MarkNotified(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            var added = false;
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (_keySet.Add(key))
                {
                    _orderedKeys.Add(key);
                    added = true;
                }
            }

            if (!added) return;

            while (_orderedKeys.Count > MaxKeys)
            {
                var oldest = _orderedKeys[0];
                _orderedKeys.RemoveAt(0);
                _keySet.Remove(oldest);
            }

            SaveLocked();
        }
    }

    private void EnsureLoadedLocked()
    {
        if (_loaded) return;
        _loaded = true;

        if (!File.Exists(_path)) return;

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path));
            if (list is null) return;

            foreach (var key in list)
            {
                if (!string.IsNullOrWhiteSpace(key) && _keySet.Add(key))
                {
                    _orderedKeys.Add(key);
                }
            }

            while (_orderedKeys.Count > MaxKeys)
            {
                var oldest = _orderedKeys[0];
                _orderedKeys.RemoveAt(0);
                _keySet.Remove(oldest);
            }
        }
        catch (JsonException)
        {
            // Tệp hỏng: giữ an toàn, bắt đầu lại danh sách sạch
            _orderedKeys.Clear();
            _keySet.Clear();
        }
    }

    private void SaveLocked()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_orderedKeys));
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>
/// Bản giả trong bộ nhớ cho --demo và UiTests.
/// </summary>
public sealed class InMemoryAlertNotifyStateStore : IAlertNotifyStateStore
{
    private readonly List<string> _orderedKeys = new();
    private readonly HashSet<string> _keySet = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate) return _orderedKeys.Count;
        }
    }

    public bool HasNotified(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_gate) return _keySet.Contains(key);
    }

    public void MarkNotified(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (_keySet.Add(key))
                {
                    _orderedKeys.Add(key);
                }
            }

            while (_orderedKeys.Count > FileAlertNotifyStateStore.MaxKeys)
            {
                var oldest = _orderedKeys[0];
                _orderedKeys.RemoveAt(0);
                _keySet.Remove(oldest);
            }
        }
    }
}
