using FamilyTree.Core.Models;

namespace FamilyTree.Core.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string firstName, string lastName, string email, string password, string? inviteToken = null);
    Task<AuthResult> LinkPersonAsync(Guid userId, Guid? personId);

    Task<InviteResult>                  CreateInviteAsync(string email);
    Task<List<UserInvite>>              GetPendingInvitesAsync();
    Task<AuthResult>                    CancelInviteAsync(Guid inviteId);
    Task<UserInvite?>                   ValidateInviteTokenAsync(string token);
    string                              GetRegistrationMode();

    Task                                SaveFocusPersonAsync(Guid userId, Guid? personId);
    Task<Guid?>                         GetFocusPersonIdAsync(Guid userId);

    Task<AuthResult>                    RequestPasswordResetAsync(string email, string? baseUrl = null);
    Task<AuthResult>                    ResetPasswordAsync(string email, string token, string newPassword);
    Task<List<PasswordResetRequest>>    GetPendingResetRequestsAsync();
    Task                                DismissResetRequestAsync(Guid id);
}

public record AuthResult(bool Success, string? Error = null, Guid? UserId = null);
public record InviteResult(bool Success, string? Error = null, string? Token = null);
