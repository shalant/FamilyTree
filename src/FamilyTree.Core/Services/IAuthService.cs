using FamilyTree.Core.Models;

namespace FamilyTree.Core.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string firstName, string lastName, string email, string password, Guid? inviteId = null);
    Task<AuthResult> LinkPersonAsync(Guid userId, Guid? personId);

    Task<InviteResult>                  CreateInviteAsync(string email);
    Task<List<UserInvite>>              GetPendingInvitesAsync();
    Task<AuthResult>                    CancelInviteAsync(Guid inviteId);
    Task<UserInvite?>                   ValidateInviteAsync(Guid inviteId);
    string                              GetRegistrationMode();

    Task                                SaveFocusPersonAsync(Guid userId, Guid? personId);
    Task<Guid?>                         GetFocusPersonIdAsync(Guid userId);

    Task<AuthResult>                    RequestPasswordResetAsync(string email, string? baseUrl = null);
    Task<bool>                          IsResetRequestValidAsync(Guid requestId);
    Task<AuthResult>                    ResetPasswordAsync(Guid requestId, string newPassword);
    Task<List<PasswordResetRequest>>    GetPendingResetRequestsAsync();
    Task                                DismissResetRequestAsync(Guid id);

    Task<AuthResult>                    LinkUserToTreeAsync(
        Guid userId,
        Guid? personId,
        string? firstName,
        string? lastName,
        Guid? connectedPersonId = null,
        string? relationshipType = null);

    /// <summary>
    /// Creates a standalone Person (not linked to any user) connected to an existing tree
    /// member via a relationship — used to add a story subject (e.g. "Bill") to the tree
    /// before the inviting user (e.g. "Willa") links herself to him.
    /// </summary>
    Task<AuthResult>                    CreateUnlinkedPersonAsync(
        string? firstName,
        string? lastName,
        Guid connectedPersonId,
        string relationshipType,
        Guid createdByUserId);
}

public record AuthResult(bool Success, string? Error = null, Guid? UserId = null, Guid? PersonId = null);
public record InviteResult(bool Success, string? Error = null, Guid? Id = null);
