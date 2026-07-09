using FamilyTree.Core.Models;

namespace FamilyTree.Core.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string firstName, string lastName, string email, string password, Guid? inviteId = null);
    Task<AuthResult> LinkPersonAsync(Guid userId, Guid? personId);

    /// <summary>Does an account already exist for this email? Used to decide whether
    /// a story-invite respondent should be offered "sign in" vs. "create account."</summary>
    Task<bool> UserExistsAsync(string email);

    /// <summary>
    /// Ensures a user has a UserFamily row, defaulting to the single family this
    /// deployment currently has (creating it if missing) if they don't already have
    /// one. No-op if the user is already assigned. Used for registration paths that
    /// don't go through RegisterAsync (e.g. the Google OAuth new-account branch).
    /// </summary>
    Task EnsureUserFamilyAsync(Guid userId);

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

    /// <summary>
    /// Of the given candidate Person IDs, returns which are already linked to an AppUser
    /// account — used to filter "is this you?" duplicate-match candidates down to people
    /// who could actually be claimed (LinkUserToTreeAsync would otherwise reject an
    /// already-claimed person via IX_AspNetUsers_PersonId).
    /// </summary>
    Task<HashSet<Guid>>                 GetLinkedPersonIdsAsync(IEnumerable<Guid> personIds);
}

public record AuthResult(bool Success, string? Error = null, Guid? UserId = null, Guid? PersonId = null);
public record InviteResult(bool Success, string? Error = null, Guid? Id = null);
