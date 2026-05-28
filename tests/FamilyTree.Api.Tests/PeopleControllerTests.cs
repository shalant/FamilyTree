using FamilyTree.Api.Controllers;
using FamilyTree.Api.Data;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;

namespace FamilyTree.Api.Tests;

public class PeopleControllerTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoPeople()
    {
        using var db = CreateInMemoryDb();
        var controller = new PeopleController(db);

        var result = await controller.GetAll();

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ThenGetById_ReturnsSamePerson()
    {
        using var db = CreateInMemoryDb();
        var controller = new PeopleController(db);

        var request = new CreatePersonRequest(
            "Jane", null, "Smith", null,
            new DateOnly(1980, 6, 15), "Chicago", null, null, null, Gender.Female
        );

        var createResult = await controller.Create(request);
        var created = (createResult.Result as CreatedAtActionResult)?.Value as PersonDto;

        created.Should().NotBeNull();
        created!.FirstName.Should().Be("Jane");
        created.FullName.Should().Be("Jane Smith");

        var getResult = await controller.GetById(created.Id);
        getResult.Value.Should().NotBeNull();
        getResult.Value!.Id.Should().Be(created.Id);
    }
}
