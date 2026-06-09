using FamilyTree.Core.Models;
using FamilyTree.Shared;

namespace FamilyTree.Core.Services;

public interface IUserMessageService
{
    Task<ServiceResponse> SubmitFeatureRequestAsync(
        string title, string category, string description, string priority, string? senderEmail);

    Task<ServiceResponse> SubmitContactMessageAsync(
        string subject, string body, string? senderEmail);

    Task<ServiceResponse<List<UserMessage>>> GetAllAsync();
    Task MarkReadAsync(Guid id);
}
