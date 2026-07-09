using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Core.Services;

public interface IPersonService
{
    Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Family-scoped like GetAllAsync, but resolves the family from the given user's own
    /// UserFamily row instead of the ambient ICurrentUserService — for the brief window
    /// during registration where a new account exists but the browser isn't authenticated
    /// yet (LinkToTreeModal's person picker, shown before the post-registration login POST).
    /// </summary>
    Task<ServiceResponse<List<PersonDto>>> GetAllForUserAsync(Guid userId, CancellationToken ct = default);
    Task<ServiceResponse<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResponse<PersonDto>> CreateAsync(PersonUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse<PersonDto>> UpdateAsync(Guid id, PersonUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResponse> RestoreAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResponse<List<PersonDto>>> GetDeletedAsync(CancellationToken ct = default);
}
