using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Core.Models;

namespace FamilyTree.Core.Services;

public class AdminUserDto
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public bool IsSuperUser { get; set; }
    public Guid? PersonId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class AdminInviteDto
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class AdminPasswordResetDto
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public interface IAdminUserService
{
    Task<ServiceResponse<List<AdminUserDto>>> GetAllUsersAsync(CancellationToken ct = default);
    Task<ServiceResponse<List<AdminInviteDto>>> GetPendingInvitesAsync(CancellationToken ct = default);
    Task<ServiceResponse<List<AdminPasswordResetDto>>> GetPendingPasswordResetsAsync(CancellationToken ct = default);
    Task<ServiceResponse<List<PersonDto>>> GetPeopleAsync(CancellationToken ct = default);
    Task<ServiceResponse> CancelInviteAsync(Guid inviteId, CancellationToken ct = default);
    Task<ServiceResponse> DismissPasswordResetAsync(Guid resetId, CancellationToken ct = default);
}
