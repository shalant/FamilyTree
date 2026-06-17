using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;

namespace FamilyTree.Core.Services;

public interface IStoryService
{
    Task<ServiceResponse<List<StoryDto>>> GetByPersonAsync(Guid personId, CancellationToken ct = default);
    Task<ServiceResponse<StoryDto>> CreateAsync(StoryUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse<StoryDto>> UpdateAsync(Guid id, StoryUpsertDto dto, CancellationToken ct = default);
    Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResponse<List<StoryDto>>> GetUnlinkedAsync(CancellationToken ct = default);
    Task<ServiceResponse<StoryDto>> LinkToPersonAsync(Guid storyId, Guid personId, CancellationToken ct = default);
    Task<ServiceResponse<List<StoryDto>>> GetPendingApprovalAsync(CancellationToken ct = default);
    Task<ServiceResponse<StoryDto>> ApproveAsync(Guid storyId, CancellationToken ct = default);
}
