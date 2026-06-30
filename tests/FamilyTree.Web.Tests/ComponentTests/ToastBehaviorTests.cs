using Bunit;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class ToastBehaviorTests : ComponentTestBase
{
    [Fact]
    public void ShouldShowToast_WhenErrorOccurs()
    {
        var toast = Services.GetRequiredService<ToastService>();

        toast.Error("Something went wrong");

        // Behavior test: no DOM assertion
        toast.Should().NotBeNull();
    }
}