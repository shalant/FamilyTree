using FamilyTree.Core.Services;
using FamilyTree.Web.Modules.Admin.Components;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review A5 (docs/uxui/, 2026-11-17): the Audit log and Activity tabs showed a user as the
// first eight characters of their id. They show the name an admin would recognise.
public class AdminUserNamesTests
{
    private static readonly Guid Doug = Guid.Parse("f9b335bf-0000-0000-0000-000000000001");
    private static readonly Guid NoName = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000002");
    private static readonly Guid Gone = Guid.Parse("0badc0de-0000-0000-0000-000000000003");

    private readonly AdminUserNames _names = new(
    [
        new AdminUserDto { Id = Doug, Email = "doug@example.com", DisplayName = "Doug Rosenberg" },
        new AdminUserDto { Id = NoName, Email = "relative@example.com", DisplayName = "" },
    ]);

    [Fact]
    public void KnownUser_ShowsTheirDisplayName() => _names.Label(Doug).Should().Be("Doug Rosenberg");

    [Fact]
    public void UserWithNoDisplayName_ShowsTheirEmail() => _names.Label(NoName).Should().Be("relative@example.com");

    [Fact]
    public void UnknownUser_FallsBackToTheShortId() => _names.Label(Gone).Should().Be("0badc0de…");

    [Fact]
    public void NoUser_ShowsADash() => _names.Label(null).Should().Be("—");

    [Fact]
    public void EmptyLookup_ShowsShortIds_SoAFailedLoadChangesNothing() =>
        AdminUserNames.Empty.Label(Doug).Should().Be("f9b335bf…");
}
