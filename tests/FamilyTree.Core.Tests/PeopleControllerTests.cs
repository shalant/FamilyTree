using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using Xunit;
using FamilyTree.Shared.DTOs.Person;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FamilyTree.Core.Tests;

public class PersonServiceTests
{
    private static (PersonService service, TestDbContextFactory factory, FakeCurrentUserService fakeUser)
        CreateSut(Guid? userId = null, Guid? familyId = null)
    {
        var factory = new TestDbContextFactory();
        var fakeUser = new FakeCurrentUserService { UserId = userId, FamilyId = familyId };
        var service = new PersonService(
            factory,
            NullLogger<PersonService>.Instance,
            new FakeAuditLogService(),
            fakeUser);
        return (service, factory, fakeUser);
    }

    [Fact]
    public async Task CreateAsync_ValidPerson_ReturnsPerson()
    {
        var (service, _, _) = CreateSut();

        var result = await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "Smith" });

        result.Success.Should().BeTrue();
        result.Data!.FullName.Should().Contain("Alice");
    }

    [Fact]
    public async Task CreateAsync_StampsCreatedByAndFamilyId()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId, familyId);

        var result = await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Jones" });

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var person = ctx.People.Single();
        person.CreatedBy.Should().Be(userId);
        person.FamilyId.Should().Be(familyId);
    }

    [Fact]
    public async Task CreateAsync_WhitespaceFirstName_ReturnsFail()
    {
        var (service, _, _) = CreateSut();

        var result = await service.CreateAsync(new PersonUpsertDto { FirstName = "   ", LastName = "Smith" });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_BirthDateAfterDeathDate_ReturnsFail()
    {
        var (service, _, _) = CreateSut();
        var dto = new PersonUpsertDto
        {
            FirstName = "Alice",
            LastName = "Smith",
            BirthDate = new DateOnly(1990, 1, 1),
            DeathDate = new DateOnly(1980, 1, 1),
        };

        var result = await service.CreateAsync(dto);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Birth date");
    }

    [Fact]
    public async Task DeleteAsync_SetsDeletedAtAndDeletedBy()
    {
        var userId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId);
        var created = await service.CreateAsync(new PersonUpsertDto { FirstName = "Carol", LastName = "White" });
        var personId = created.Data!.Id;

        var result = await service.DeleteAsync(personId);

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var person = ctx.People.IgnoreQueryFilters().Single(p => p.Id == personId);
        person.DeletedAt.Should().NotBeNull();
        person.DeletedBy.Should().Be(userId);
    }

    [Fact]
    public async Task RestoreAsync_ClearsDeletedAtAndDeletedBy()
    {
        var userId = Guid.NewGuid();
        var (service, factory, _) = CreateSut(userId);
        var created = await service.CreateAsync(new PersonUpsertDto { FirstName = "Dave", LastName = "Brown" });
        var personId = created.Data!.Id;
        await service.DeleteAsync(personId);

        var result = await service.RestoreAsync(personId);

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var person = ctx.People.Single(p => p.Id == personId);
        person.DeletedAt.Should().BeNull();
        person.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_FamilyScopingFiltersToMatchingFamily()
    {
        var family1 = Guid.NewGuid();
        var family2 = Guid.NewGuid();
        var (service, _, fakeUser) = CreateSut(familyId: family1);

        await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "FamilyOne" });
        fakeUser.FamilyId = family2;
        await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "FamilyTwo" });

        fakeUser.FamilyId = family1;
        var result = await service.GetAllAsync();

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(1);
        result.Data![0].LastName.Should().Be("FamilyOne");
    }

    [Fact]
    public async Task GetAllAsync_NoFamilyId_ReturnsAllPeople()
    {
        var (service, _, fakeUser) = CreateSut(familyId: Guid.NewGuid());

        await service.CreateAsync(new PersonUpsertDto { FirstName = "Alice", LastName = "Smith" });
        fakeUser.FamilyId = Guid.NewGuid();
        await service.CreateAsync(new PersonUpsertDto { FirstName = "Bob", LastName = "Jones" });

        fakeUser.FamilyId = null;
        var result = await service.GetAllAsync();

        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(2);
    }
}
