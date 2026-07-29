using FamilyTree.Core.Models;
using FamilyTree.Shared;

namespace FamilyTree.Core.Services;

public interface IAuthService
{
    Task<ServiceResponse<Guid>> RegisterAsync(string firstName, string lastName, string email, string password, Guid? inviteId = null);
    Task<ServiceResponse> LinkPersonAsync(Guid userId, Guid? personId);

    /// <summary>Does an account already exist for this email? Used to decide whether
    /// a story-invite respondent should be offered "sign in" vs. "create account."</summary>
    Task<ServiceResponse<bool>> UserExistsAsync(string email);

    /// <summary>
    /// Ensures a user has a UserFamily row, defaulting to the single family this
    /// deployment currently has (creating it if missing) if they don't already have
    /// one. No-op if the user is already assigned. Used for registration paths that
    /// don't go through RegisterAsync (e.g. the Google OAuth new-account branch).
    /// </summary>
    Task<ServiceResponse> EnsureUserFamilyAsync(Guid userId);

    Task<ServiceResponse<Guid>>                  CreateInviteAsync(string email);
    Task<ServiceResponse<List<UserInvite>>>      GetPendingInvitesAsync();
    Task<ServiceResponse>                        CancelInviteAsync(Guid inviteId);
    Task<ServiceResponse<UserInvite?>>           ValidateInviteAsync(Guid inviteId);
    string                                       GetRegistrationMode();

    Task<ServiceResponse>                        SaveFocusPersonAsync(Guid userId, Guid? personId);
    Task<ServiceResponse<Guid?>>                 GetFocusPersonIdAsync(Guid userId);

    Task<ServiceResponse>                        RequestPasswordResetAsync(string email, string? baseUrl = null);
    Task<ServiceResponse<bool>>                  IsResetRequestValidAsync(Guid requestId);
    Task<ServiceResponse>                        ResetPasswordAsync(Guid requestId, string newPassword);
    Task<ServiceResponse<List<PasswordResetRequest>>>    GetPendingResetRequestsAsync();
    Task<ServiceResponse>                        DismissResetRequestAsync(Guid id);

    Task<ServiceResponse<Guid?>>                 LinkUserToTreeAsync(
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
    Task<ServiceResponse<Guid>>                  CreateUnlinkedPersonAsync(
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
    Task<ServiceResponse<HashSet<Guid>>>        GetLinkedPersonIdsAsync(IEnumerable<Guid> personIds);
}
