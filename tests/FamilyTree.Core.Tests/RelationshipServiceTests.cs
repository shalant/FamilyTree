using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using Xunit;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FamilyTree.Core.Tests;

public class RelationshipServiceTests
{
    private static readonly Guid LowerGuid  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid HigherGuid = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static (RelationshipService service, TestDbContextFactory factory) CreateSut()
    {
        var factory = new TestDbContextFactory();
        var service = new RelationshipService(
            factory,
            NullLogger<RelationshipService>.Instance,
            new FakeAuditLogService(),
            new FakeCurrentUserService());
        return (service, factory);
    }

    [Fact]
    public async Task CreateAsync_AlwaysStoresLowerGuidAsPersonA()
    {
        var (service, factory) = CreateSut();

        // Pass in reversed order: higher as PersonA, lower as PersonB
        var result = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = HigherGuid,
            PersonBId = LowerGuid,
            Type = RelationshipType.Sibling,
        });

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        var rel = ctx.Relationships.Single();
        rel.PersonAId.Should().Be(LowerGuid);
        rel.PersonBId.Should().Be(HigherGuid);
    }

    [Fact]
    public async Task CreateAsync_DuplicateRelationship_ReturnsFail()
    {
        var (service, _) = CreateSut();
        var dto = new RelationshipUpsertDto
        {
            PersonAId = LowerGuid,
            PersonBId = HigherGuid,
            Type = RelationshipType.Sibling,
        };

        var first = await service.CreateAsync(dto);
        var second = await service.CreateAsync(dto);

        first.Success.Should().BeTrue();
        second.Success.Should().BeFalse();
        second.Message.Should().Contain("already exists");
    }

    [Fact]
    public async Task DeleteAsync_RemovesRelationship()
    {
        var (service, factory) = CreateSut();
        var created = await service.CreateAsync(new RelationshipUpsertDto
        {
            PersonAId = LowerGuid,
            PersonBId = HigherGuid,
            Type = RelationshipType.Sibling,
        });

        var result = await service.DeleteAsync(created.Data!.Id);

        result.Success.Should().BeTrue();
        await using var ctx = factory.CreateDbContext();
        ctx.Relationships.Should().BeEmpty();
    }
}
