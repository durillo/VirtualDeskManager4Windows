using System.Globalization;
using System.Text;

namespace VirtualDeskManager.Core.Diagnostics;

/// <summary>
/// Rolling one-file-per-day structured logger. Writing a log line must never be able to take
/// the application down, so every failure is swallowed (spec section 35).
/// </summary>
public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private const int DefaultRetainedFiles = 7;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly int _retainedFiles;

    private StreamWriter? _writer;
    private DateTime _currentDay = DateTime.MinValue;
    private bool _disposed;

    /// <summary>Crea el diario. La carpeta no se toca hasta escribir la primera línea.</summary>
    /// <param name="directory">Carpeta de los ficheros <c>vdm-aaaammdd.log</c>.</param>
    /// <param name="minimumLevel">Nivel mínimo que se escribe.</param>
    /// <param name="retainedFiles">Cuántos días de ficheros se conservan; al menos uno.</param>
    public FileAppLogger(string directory, LogLevel minimumLevel = LogLevel.Information, int retainedFiles = DefaultRetainedFiles)
    {
        _directory = directory;
        _retainedFiles = Math.Max(1, retainedFiles);
        MinimumLevel = minimumLevel;
    }

    /// <summary>Las líneas que se guardan en memoria para la pestaña del diario.</summary>
    public const int RecentCapacity = 3000;

    private readonly Queue<string> _recent = new();

    /// <inheritdoc/>
    public LogLevel MinimumLevel { get; set; }

    /// <summary>Se acaba de escribir una línea. Llega en el hilo que la escribió.</summary>
    public event Action<string>? LineWritten;

    /// <summary>Las últimas líneas escritas, de la más vieja a la más nueva.</summary>
    public IReadOnlyList<string> Recent()
    {
        lock (_gate)
        {
            return _recent.ToArray();
        }
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level) => level >= MinimumLevel && MinimumLevel != LogLevel.None;

    /// <summary>
    /// Escribe la línea en el fichero del día, la guarda entre las recientes y avisa por
    /// <see cref="LineWritten"/>. Si el disco falla, la línea se pierde sin lanzar nada.
    /// </summary>
    public void Log(
        LogLevel level,
        string eventName,
        string? message,
        Exception? exception,
        params (string Key, object? Value)[] properties)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        string line = Format(level, eventName, message, exception, properties);

        System.Diagnostics.Debug.WriteLine(line);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _recent.Enqueue(line);

            while (_recent.Count > RecentCapacity)
            {
                _recent.Dequeue();
            }

            try
            {
                StreamWriter writer = GetWriter();
                writer.WriteLine(line);
            }
            catch
            {
                // A logger that throws is worse than a missing log line.
            }
        }

        try
        {
            LineWritten?.Invoke(line);
        }
        catch
        {
            // Lo mismo: quien escucha no puede tumbar al que escribe.
        }
    }

    /// <summary>La carpeta de los ficheros del diario.</summary>
    public string LogDirectory => _directory;

    private static string Format(
        LogLevel level,
        string eventName,
        string? message,
        Exception? exception,
        (string Key, object? Value)[] properties)
    {
        StringBuilder builder = new();
        builder.Append('[')
               .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
               .Append(' ')
               .Append(Abbreviate(level))
               .Append("] ")
               .Append(eventName);

        if (!string.IsNullOrWhiteSpace(message))
        {
            builder.Append(" | ").Append(message);
        }

        foreach ((string Key, object? Value) property in properties)
        {
            builder.Append(' ')
                   .Append(property.Key)
                   .Append('=')
                   .Append(FormatValue(property.Value));
        }

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        return builder.ToString();
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string text => text.Length == 0 ? "\"\"" : text,
        nint handle => "0x" + handle.ToString("X"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "___"
    };

    private StreamWriter GetWriter()
    {
        DateTime today = DateTime.Today;

        if (_writer is not null && _currentDay == today)
        {
            return _writer;
        }

        _writer?.Dispose();
        _writer = null;

        Directory.CreateDirectory(_directory);

        string path = Path.Combine(_directory, $"vdm-{today:yyyyMMdd}.log");
        FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        _currentDay = today;

        PurgeOldFiles();

        return _writer;
    }

    private void PurgeOldFiles()
    {
        try
        {
            string[] files = Directory.GetFiles(_directory, "vdm-*.log");

            if (files.Length <= _retainedFiles)
            {
                return;
            }

            foreach (string file in files.OrderByDescending(f => f).Skip(_retainedFiles))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // The file may be locked by another instance; ignore.
                }
            }
        }
        catch
        {
            // Purging is best effort.
        }
    }

    /// <summary>Cierra el fichero abierto. Las líneas que lleguen después se descartan.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
