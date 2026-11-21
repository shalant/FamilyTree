using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// 2026-11-20: an Admin > Users invite can name the person it is for ("Who is this?"). Signing up
// from it links the new account to that person, so an estranged relative is never asked
// "Who are you?" and cannot pick the wrong record.
public class InviteForPersonTests
{
    private static readonly Guid FamilyA = Guid.NewGuid();
    private static readonly Guid FamilyB = Guid.NewGuid();
    private const string Password = "TestPass123!";

    private static (AuthService Svc, TestDbContextFactory Factory, UserManager<AppUser> Users) CreateSut(bool superUser = true)
    {
        var factory = new TestDbContextFactory();
        var users = TestUserManagerFactory.Create(factory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:RegistrationMode"] = "InviteOnly",
            ["Auth:InviteTtlDays"] = "7",
        }).Build();
        var svc = new AuthService(users, config, factory, new FakeEmailSender(), new NullLogger<AuthService>(),
            new FakeCurrentUserService { IsSuperUser = superUser });
        return (svc, factory, users);
    }

    private static async Task<Guid> SeedPersonAsync(TestDbContextFactory factory, Guid familyId, string first = "Cousin", string last = "Eisenberg")
    {
        await using var ctx = factory.CreateDbContext();
        var p = new Person { Id = Guid.NewGuid(), FirstName = first, LastName = last, FamilyId = familyId };
        ctx.People.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task InviteForPerson_JoinsThePersonsFamily_AndRegistrationLinksThem()
    {
        var (svc, factory, users) = CreateSut();
        var personId = await SeedPersonAsync(factory, FamilyB);

        var invite = await svc.CreateInviteAsync("cousin@example.com", personId);
        invite.Success.Should().BeTrue(invite.Message);

        var reg = await svc.RegisterAsync("Cousin", "Eisenberg", "cousin@example.com", Password, invite.Data);
        reg.Success.Should().BeTrue(reg.Message);

        (await users.FindByIdAsync(reg.Data.ToString()))!.PersonId.Should().Be(personId);
        await using var ctx = factory.CreateDbContext();
        ctx.UserFamilies.Should().ContainSingle(uf => uf.UserId == reg.Data && uf.FamilyId == FamilyB,
            "the invite uses the person's family, not the oldest one");
    }

    [Fact]
    public async Task InviteForPerson_GoogleSignIn_LinksThemToo()
    {
        var (svc, factory, users) = CreateSut();
        var personId = await SeedPersonAsync(factory, FamilyA);
        (await svc.CreateInviteAsync("cousin@example.com", personId)).Success.Should().BeTrue();

        var reg = await svc.RegisterExternalAsync("cousin@example.com", "Cousin", "Eisenberg", emailVerified: true);

        reg.Success.Should().BeTrue(reg.Message);
        (await users.FindByIdAsync(reg.Data.ToString()))!.PersonId.Should().Be(personId);
    }

    [Fact]
    public async Task InviteWithoutAPerson_LeavesTheAccountUnlinked()
    {
        var (svc, _, users) = CreateSut();
        var invite = await svc.CreateInviteAsync("someone@example.com");
        var reg = await svc.RegisterAsync("Some", "One", "someone@example.com", Password, invite.Data);

        reg.Success.Should().BeTrue(reg.Message);
        (await users.FindByIdAsync(reg.Data.ToString()))!.PersonId.Should().BeNull();
    }

    [Fact]
    public async Task CreateInvite_RefusesAPersonWhoAlreadyHasAnAccount()
    {
        var (svc, factory, users) = CreateSut();
        var personId = await SeedPersonAsync(factory, FamilyA);
        await users.CreateAsync(new AppUser { UserName = "x@example.com", Email = "x@example.com", PersonId = personId, CreatedAt = DateTime.UtcNow }, Password);

        var r = await svc.CreateInviteAsync("cousin@example.com", personId);

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("already has an account");
    }

    [Fact]
    public async Task CreateInvite_RefusesASecondOpenInviteForTheSamePerson()
    {
        var (svc, factory, _) = CreateSut();
        var personId = await SeedPersonAsync(factory, FamilyA);
        (await svc.CreateInviteAsync("one@example.com", personId)).Success.Should().BeTrue();

        var r = await svc.CreateInviteAsync("two@example.com", personId);

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("open invite");
    }

    [Fact]
    public async Task CreateInvite_RefusesAPersonWhoIsNotOnTheTree()
    {
        var (svc, _, _) = CreateSut();

        var r = await svc.CreateInviteAsync("cousin@example.com", Guid.NewGuid());

        r.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateInvite_IsSuperUserOnly_EvenWithAPerson()
    {
        var (svc, factory, _) = CreateSut(superUser: false);
        var personId = await SeedPersonAsync(factory, FamilyA);

        (await svc.CreateInviteAsync("cousin@example.com", personId)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Registration_WhenThePersonWasClaimedMeanwhile_StillSucceeds_AndStealsNothing()
    {
        var (svc, factory, users) = CreateSut();
        var personId = await SeedPersonAsync(factory, FamilyA);
        var invite = await svc.CreateInviteAsync("cousin@example.com", personId);
        var other = new AppUser { UserName = "other@example.com", Email = "other@example.com", PersonId = personId, CreatedAt = DateTime.UtcNow };
        (await users.CreateAsync(other, Password)).Succeeded.Should().BeTrue();

        var reg = await svc.RegisterAsync("Cousin", "Eisenberg", "cousin@example.com", Password, invite.Data);

        reg.Success.Should().BeTrue(reg.Message);
        (await users.FindByIdAsync(reg.Data.ToString()))!.PersonId.Should().BeNull("the person is held by another account");
        (await users.FindByIdAsync(other.Id.ToString()))!.PersonId.Should().Be(personId);
    }
}
