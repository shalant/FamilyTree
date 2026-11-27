using Bunit;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Web.Modules.Admin.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class ProblemsTabTests : ComponentTestBase
{
    private sealed class Problems(params ProblemView[] rows) : IProblemService
    {
        public Task<ServiceResponse<List<ProblemView>>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<ProblemView>>.Ok([.. rows]));
    }

    [Fact]
    public void ListsWhoWhereAndWhat()
    {
        Services.AddSingleton<IProblemService>(new Problems(new ProblemView(new Problem
        {
            UserEmail = "relative@example.com", Page = "/people/1/edit",
            Message = "You do not have permission to update this person.",
        }, "Testy McTesters")));
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<ProblemsTab>();

        cut.Markup.Should().Contain("relative@example.com").And.Contain("/people/1/edit")
            .And.Contain("Testy McTesters").And.Contain("You do not have permission")
            .And.Contain("Times are UTC");
    }

    [Fact]
    public void SaysSoWhenThereIsNothing()
    {
        Services.AddSingleton<IProblemService>(new Problems());

        RenderComponent<ProblemsTab>().Markup.Should().Contain("Nothing has gone wrong yet.");
    }
}
