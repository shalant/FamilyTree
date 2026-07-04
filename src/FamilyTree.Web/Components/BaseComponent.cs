using FamilyTree.Shared;
using FamilyTree.Web.Services;
using Microsoft.AspNetCore.Components;

namespace FamilyTree.Web.Components;

/// <summary>
/// Base component class that provides error-handling wrappers for service calls.
/// All components should inherit from this to ensure consistent error toasts.
/// </summary>
public abstract class BaseComponent : ComponentBase
{
    [Inject] protected ToastService ToastService { get; set; } = null!;

    /// <summary>
    /// Safely call a service method and show error toast if it fails.
    /// Returns the data on success, or default(T) on failure.
    /// </summary>
    protected async Task<T?> TryAsync<T>(
        Func<Task<ServiceResponse<T>>> call,
        string action = "Operation")
    {
        try
        {
            var result = await call();
            if (!result.Success)
            {
                ToastService.Error(result.Message ?? $"{action} failed");
                return default;
            }
            return result.Data;
        }
        catch (Exception ex)
        {
            ToastService.Error($"{action} failed: {ex.Message}");
            return default;
        }
    }

    /// <summary>
    /// Safely call a service method that returns no data (ServiceResponse without T).
    /// Returns success boolean. Error toast shown automatically on failure.
    /// </summary>
    protected async Task<bool> TryAsync(
        Func<Task<ServiceResponse>> call,
        string action = "Operation")
    {
        try
        {
            var result = await call();
            if (!result.Success)
            {
                ToastService.Error(result.Message ?? $"{action} failed");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            ToastService.Error($"{action} failed: {ex.Message}");
            return false;
        }
    }
}
