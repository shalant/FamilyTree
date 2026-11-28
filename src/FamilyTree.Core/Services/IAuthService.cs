using FamilyTree.Core.Models;
using FamilyTree.Shared;

namespace FamilyTree.Core.Services;

public sealed record InvitedPersonName(string FirstName, string LastName);

public interface IAuthService
{
    Task<ServiceResponse<Guid>> RegisterAsync(string firstName, string lastName, string email, string password, Guid? inviteId = null);
    Task<ServiceResponse> LinkPersonAsync(Guid userId, Guid? personId);

    /// <summary>Does an account already exist for this email? Used to decide whether
    /// a story-invite respondent should be offered "sign in" vs. "create account."</summary>
    Task<ServiceResponse<bool>> UserExistsAsync(string email);

    /// <summary>
    /// Creates the account for a first Google sign-in, in the family of the address's open
    /// invite. Invite-only mode needs such an invite and a Google-verified address; it fails
    /// with <see cref="AuthService.NoInviteForExternalSignIn"/> otherwise (A6, 2026-10-25:
    /// invitees couldn't sign up with Google at all).
    /// </summary>
    Task<ServiceResponse<Guid>> RegisterExternalAsync(string email, string firstName, string lastName, bool emailVerified);

    /// <summary>
    /// SuperUser-only admin invite (Admin › Users). With <paramref name="personId"/> the invite is
    /// for that person on the tree (in their family): signing up links the new account to them
    /// and skips the "Who are you?" step. The person must exist and have no account or open
    /// invite already.
    /// </summary>
    Task<ServiceResponse<Guid>>                  CreateInviteAsync(string email, Guid? personId = null);

    /// <summary>
    /// SuperUser-only: emails an open admin invite's register link to the address it was
    /// made for. Fails with a message the admin can act on (copy the link instead) when the
    /// send fails; the invite itself stays valid either way.
    /// </summary>
    Task<ServiceResponse>                        SendInviteEmailAsync(Guid inviteId, string baseUrl);

    /// <summary>
    /// Registration invite offered to someone who just answered a story invite. Not
    /// caller-gated (the respond page is anonymous); <paramref name="familyId"/> is the
    /// family the story is about, or null for the default family.
    /// </summary>
    Task<ServiceResponse<Guid>>                  CreateInviteForStoryResponseAsync(string email, Guid? familyId);
    Task<ServiceResponse<List<UserInvite>>>      GetPendingInvitesAsync();
    Task<ServiceResponse>                        CancelInviteAsync(Guid inviteId);
    Task<ServiceResponse<UserInvite?>>           ValidateInviteAsync(Guid inviteId);
    /// <summary>
    /// The name of the person an open invite is for, to pre-fill the sign-up form. Anonymous:
    /// only the holder of the invite ID can ask. Null when the invite names nobody, is no longer
    /// open, or the person has since been claimed or removed.
    /// </summary>
    Task<ServiceResponse<InvitedPersonName?>>    GetInvitedPersonNameAsync(Guid inviteId);
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
