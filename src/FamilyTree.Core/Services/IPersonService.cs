using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Core.Services;

public interface IPersonService
{
    Task<ServiceResponse<List<PersonDto>>> GetAllAsync(CancellationToken ct = default);
    Task<ServiceResponse<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResponse<PersonDto>> CreateAsync(PersonUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse<PersonDto>> UpdateAsync(Guid id, PersonUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default);
}