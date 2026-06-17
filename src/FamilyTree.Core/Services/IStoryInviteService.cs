using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Shared.DTOs.StoryInvite;

namespace FamilyTree.Core.Services;

public interface IStoryInviteService
{
    Task<ServiceResponse> CreateInviteAsync(StoryInviteCreateDto dto, string baseUrl, CancellationToken ct = default);
    Task<ServiceResponse<StoryInviteValidationDto>> ValidateTokenAsync(string token, CancellationToken ct = default);
    Task<ServiceResponse<StoryDto>> SubmitResponseAsync(StoryInviteResponseDto dto, CancellationToken ct = default);
    Task<ServiceResponse<List<StoryInviteAdminDto>>> GetAllAsync(CancellationToken ct = default);
}
