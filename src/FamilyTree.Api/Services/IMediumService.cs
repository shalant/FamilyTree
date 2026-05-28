using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;

namespace FamilyTree.Api.Services;

public interface IMediumService
{
    Task<ServiceResponse<List<MediumDto>>> GetAllAsync(CancellationToken ct = default);
    Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(Guid personId, CancellationToken ct = default);
    Task<ServiceResponse<MediumDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResponse<MediumDto>> CreateAsync(MediumUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse<MediumDto>> UpdateAsync(Guid id, MediumUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default);
}
