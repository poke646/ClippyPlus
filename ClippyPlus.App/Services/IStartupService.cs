namespace ClippyPlus.App.Services;

public interface IStartupService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
