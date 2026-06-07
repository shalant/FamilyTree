using FamilyTree.Shared;

namespace FamilyTree.Web.Services;

public enum ToastType { Success, Error, Warning, Info }

public sealed record ToastMessage(
    Guid Id,
    ToastType Type,
    string Message,
    string? Title,
    int Duration);

public sealed class ToastService
{
    private readonly List<ToastMessage> _toasts = [];
    public IReadOnlyList<ToastMessage> Toasts => _toasts;
    public event Action? OnChanged;

    public void Show(ToastType type, string message, string? title = null, int? duration = null)
    {
        var ms = duration ?? (type == ToastType.Error ? 8000 : 5000);
        _toasts.Add(new ToastMessage(Guid.NewGuid(), type, message, title, ms));
        OnChanged?.Invoke();
    }

    public void Success(string message, string? title = null) => Show(ToastType.Success, message, title);
    public void Error(string message, string? title = null)   => Show(ToastType.Error,   message, title);
    public void Warning(string message, string? title = null) => Show(ToastType.Warning, message, title);
    public void Info(string message, string? title = null)    => Show(ToastType.Info,    message, title);

    public void ShowResult(ServiceResponse response, string successMessage)
    {
        if (response.Success) Success(successMessage);
        else Error(response.Message);
    }

    public void ShowResult<T>(ServiceResponse<T> response, string successMessage)
    {
        if (response.Success) Success(successMessage);
        else Error(response.Message);
    }

    internal void Remove(Guid id)
    {
        _toasts.RemoveAll(t => t.Id == id);
    }
}
