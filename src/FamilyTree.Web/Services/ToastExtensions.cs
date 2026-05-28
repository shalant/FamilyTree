using FamilyTree.Shared;
using MudBlazor;

namespace FamilyTree.Web.Services;

public static class ToastExtensions
{
    public static void ShowResult(this ISnackbar snackbar,
        ServiceResponse response,
        string successMessage)
    {
        if (response.Success)
            snackbar.Add(successMessage, Severity.Success);
        else
            snackbar.Add(response.Message, Severity.Error);
    }

    public static void ShowResult<T>(this ISnackbar snackbar,
        ServiceResponse<T> response,
        string successMessage)
    {
        if (response.Success)
            snackbar.Add(successMessage, Severity.Success);
        else
            snackbar.Add(response.Message, Severity.Error);
    }
}