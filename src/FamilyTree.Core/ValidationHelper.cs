using System.ComponentModel.DataAnnotations;
using FamilyTree.Shared;

namespace FamilyTree.Core;

public static class ValidationHelper
{
    /// <summary>
    /// Validates a DTO using Data Annotation attributes.
    /// </summary>
    public static ServiceResponse<T> ValidateDto<T>(T dto) where T : class
    {
        var context = new ValidationContext(dto, serviceProvider: null, items: null);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(dto, context, results, validateAllProperties: true))
        {
            return ServiceResponse<T>.Ok(dto);
        }

        var errors = string.Join(
            "; ",
            results.SelectMany(r => r.MemberNames.DefaultIfEmpty(nameof(dto)))
                   .Zip(results.Select(r => r.ErrorMessage),
                        (member, message) => $"{member}: {message}")
        );

        return ServiceResponse<T>.Fail(errors);
    }

    /// <summary>
    /// Validates a non-generic DTO.
    /// </summary>
    public static ServiceResponse ValidateDtoNonGeneric<T>(T dto) where T : class
    {
        var context = new ValidationContext(dto, serviceProvider: null, items: null);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(dto, context, results, validateAllProperties: true))
        {
            return ServiceResponse.Ok();
        }

        var errors = string.Join(
            "; ",
            results.SelectMany(r => r.MemberNames.DefaultIfEmpty(nameof(dto)))
                   .Zip(results.Select(r => r.ErrorMessage),
                        (member, message) => $"{member}: {message}")
        );

        return ServiceResponse.Fail(errors);
    }
}
