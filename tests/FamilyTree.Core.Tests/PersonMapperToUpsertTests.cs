using FamilyTree.Core.Mappers;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

// PersonService.UpdateAsync replaces every field and link with what it's sent, so a
// field PersonMapper.ToUpsertDto doesn't copy is wiped by any save built from it. Until
// 2026-10-25 the pages built the DTO by hand: PersonEdit cleared Email on every save, and
// PersonMedia's set-photo deleted former marriages and sibling links (B7 review).
public class PersonMapperToUpsertTests
{
    [Fact]
    public void ToUpsertDto_CopiesEveryFieldTheUpdateWrites()
    {
        var person = new PersonDto
        {
            Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Arden", MiddleName = "Mae", MaidenName = "Brook",
            BirthDate = new DateOnly(1950, 2, 3), BirthPlace = "Leeds", DeathDate = new DateOnly(2020, 4, 5),
            DeathPlace = "York", BiographyNotes = "Kept bees.", ProfilePhotoUrl = "/media/a.jpg", Email = "ada@example.com",
            Gender = Gender.Female, ParentIds = [Guid.NewGuid()], SpouseIds = [Guid.NewGuid()],
            FormerSpouseIds = [Guid.NewGuid()], ChildIds = [Guid.NewGuid()], SiblingIds = [Guid.NewGuid()],
        };

        var dto = PersonMapper.ToUpsertDto(person);

        foreach (var prop in typeof(PersonUpsertDto).GetProperties())
        {
            var source = typeof(PersonDto).GetProperty(prop.Name);
            source.Should().NotBeNull($"PersonDto has no {prop.Name} to fill the form's {prop.Name} from");
            var expected = source!.GetValue(person);
            expected.Should().NotBeNull($"the test must give PersonDto.{prop.Name} a value, or it proves nothing");
            prop.GetValue(dto).Should().BeEquivalentTo(expected, $"{prop.Name} must survive a save built from ToUpsertDto");
        }
    }

    [Fact]
    public void ToUpsertDto_ListsAreCopies_SoTheFormCantChangeTheLoadedPerson()
    {
        var person = new PersonDto { FirstName = "Ada", LastName = "Arden", SiblingIds = [Guid.NewGuid()] };

        PersonMapper.ToUpsertDto(person).SiblingIds.Clear();

        person.SiblingIds.Should().HaveCount(1);
    }
}
