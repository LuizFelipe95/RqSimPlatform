using System.Collections.Concurrent;
using System.Text;


namespace RqSimUI.FormSimAPI.Interfaces;

/// <summary>
/// Helper class for buffered, throttled console output to prevent UI hangs
/// </summary>
public sealed class ConsoleBuffer : IDisposable
{
    private readonly TextBox _textBox;
    private readonly CheckBox? _autoScrollCheckBox;
    private readonly ConcurrentQueue<string> _msgQueue = new();
    private volatile int _updatePending;
    private DateTime _lastFlush = DateTime.MinValue;

    private const int MaxLineWidth = 120;
    private const int MaxConsoleLength = 100000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    public ConsoleBuffer(TextBox textBox, CheckBox? autoScrollCheckBox = null)
    {
        _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));
        _autoScrollCheckBox = autoScrollCheckBox;
    }

    /// <summary>
    /// Appends text to console with buffering and throttling.
    /// Thread-safe, can be called from any thread.
    /// </summary>
    public void Append(string text)
    {
        if (_textBox.IsDisposed)
            return;

        string formatted = FormatMessage(text);
        _msgQueue.Enqueue(formatted);

        // Throttle updates - only one pending at a time
        if (Interlocked.CompareExchange(ref _updatePending, 1, 0) == 0)
        {
            ScheduleFlush();
        }
    }

    private void ScheduleFlush()
    {
        if (_textBox.IsDisposed)
            return;

        if (_textBox.InvokeRequired)
        {
            if (!_textBox.IsHandleCreated) return;
            _textBox.BeginInvoke(new Action(Flush));
        }
        else
        {
            Flush();
        }
    }

    private void Flush()
    {
        if (_textBox.IsDisposed)
            return;

        var now = DateTime.UtcNow;
        if (now - _lastFlush < FlushInterval && _msgQueue.Count < 10)
        {
            Interlocked.Exchange(ref _updatePending, 0);
            return;
        }
        _lastFlush = now;

        var sb = new StringBuilder();
        while (_msgQueue.TryDequeue(out var msg))
        {
            sb.Append(msg);
            if (sb.Length > 5000) break;
        }

        if (sb.Length > 0)
        {
            // Truncate if too long
            if (_textBox.Text.Length > MaxConsoleLength)
            {
                _textBox.Text = _textBox.Text.Substring(_textBox.Text.Length - MaxConsoleLength / 2);
            }

            _textBox.AppendText(sb.ToString());

            if (_autoScrollCheckBox != null && !_autoScrollCheckBox.IsDisposed && _autoScrollCheckBox.Checked)
            {
                _textBox.SelectionStart = _textBox.Text.Length;
                _textBox.ScrollToCaret();
            }
        }

        Interlocked.Exchange(ref _updatePending, 0);

        // Schedule another flush if more messages accumulated
        if (!_msgQueue.IsEmpty)
        {
            Task.Delay(50).ContinueWith(_ =>
            {
                if (!_textBox.IsDisposed)
                    ScheduleFlush();
            });
        }
    }

    /// <summary>
    /// Formats a console message: wraps long lines, truncates excessive content.
    /// </summary>
    public static string FormatMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Truncate very long single-line messages
        if (text.Length > 500 && !text.Contains('\n'))
        {
            text = text.Substring(0, 400) + "... [truncated]\n";
        }

        // Wrap lines exceeding max width
        var lines = text.Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.Length <= MaxLineWidth)
            {
                sb.AppendLine(line.TrimEnd());
            }
            else
            {
                // Wrap long line at word boundaries if possible
                int pos = 0;
                while (pos < line.Length)
                {
                    int len = Math.Min(MaxLineWidth, line.Length - pos);

                    // Try to break at space if possible
                    if (pos + len < line.Length && len > 20)
                    {
                        int lastSpace = line.LastIndexOf(' ', pos + len - 1, len);
                        if (lastSpace > pos)
                            len = lastSpace - pos + 1;
                    }

                    sb.AppendLine(line.Substring(pos, len).TrimEnd());
                    pos += len;
                }
            }
        }

        string result = sb.ToString().TrimEnd();
        if (!result.EndsWith("\n")) result += "\n";
        return result;
    }

    public void Dispose()
    {
        // Drain remaining messages
        while (_msgQueue.TryDequeue(out _)) { }
    }
}
