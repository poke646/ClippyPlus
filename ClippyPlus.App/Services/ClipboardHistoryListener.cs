using Avalonia.Threading;

namespace ClippyPlus.App.Services;

public sealed class ClipboardHistoryListener : IClipboardHistoryListener
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly IClipboardHistoryStore _store;
    private readonly IClipboardService _clipboardService;
    private Timer? _timer;
    private int _ignoreDepth;
    private string? _lastHash;

    public ClipboardHistoryListener(IClipboardHistoryStore store, IClipboardService clipboardService)
    {
        _store = store;
        _clipboardService = clipboardService;
    }

    public bool IsTemporarilyIgnoringClipboardChanges => _ignoreDepth > 0;

    public void Start()
    {
        _timer ??= new Timer(
            static state => _ = ((ClipboardHistoryListener)state!).CaptureCurrentClipboardAsync(),
            this,
            TimeSpan.Zero,
            PollInterval);
    }

    public IDisposable IgnoreClipboardChanges()
    {
        _ignoreDepth++;
        return new IgnoreScope(this);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task CaptureCurrentClipboardAsync()
    {
        if (IsTemporarilyIgnoringClipboardChanges)
        {
            return;
        }

        try
        {
            var entry = await Dispatcher.UIThread.InvokeAsync(_clipboardService.ReadCurrentEntryAsync);
            if (entry is null || entry.ContentHash == _lastHash)
            {
                return;
            }

            _lastHash = entry.ContentHash;
            _store.Upsert(entry);
        }
        catch
        {
            // Clipboard access can fail briefly while another app owns it. Polling will try again.
        }
    }

    private sealed class IgnoreScope : IDisposable
    {
        private ClipboardHistoryListener? _listener;

        public IgnoreScope(ClipboardHistoryListener listener)
        {
            _listener = listener;
        }

        public void Dispose()
        {
            if (_listener is { } listener)
            {
                listener._ignoreDepth = Math.Max(0, listener._ignoreDepth - 1);
                _listener = null;
            }
        }
    }
}
