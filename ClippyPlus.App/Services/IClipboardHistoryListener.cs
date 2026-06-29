namespace ClippyPlus.App.Services;

public interface IClipboardHistoryListener : IDisposable
{
    bool IsTemporarilyIgnoringClipboardChanges { get; }

    void Start();

    IDisposable IgnoreClipboardChanges();
}
