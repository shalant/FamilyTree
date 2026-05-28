using FamilyTree.Shared;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Relationship;

namespace FamilyTree.Api.Services;

public interface IRelationshipService
{
    Task<ServiceResponse<List<RelationshipDto>>> GetAllAsync(
        CancellationToken ct = default);

    Task<ServiceResponse<List<RelationshipDto>>> GetForPersonAsync(
        Guid personId, CancellationToken ct = default);

    Task<ServiceResponse<RelationshipDto>> CreateAsync(
        RelationshipUpsertDto dto, CancellationToken ct = default);

    Task<ServiceResponse> DeleteAsync(
        Guid id, CancellationToken ct = default);
}