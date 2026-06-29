using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public sealed class LastCaptureStore
{
    public event EventHandler? Changed;

    public CapturedImage? Image { get; private set; }

    public bool HasImage => Image is not null;

    public void Set(CapturedImage image)
    {
        Image = image;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
