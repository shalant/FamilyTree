using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

public class ToastServiceTests
{
    // Found live 2026-10-11 (smoke test): the "Tip: Drag to explore the tree" toast showed
    // twice. Double toasts have recurred from several different causes, so ToastService
    // itself refuses to stack an identical toast that is still showing.
    [Fact]
    [Trait("Category", "Regression")]
    public void IdenticalToastWhileOneIsShowing_IsNotStackedAgain()
    {
        var toasts = new ToastService();

        toasts.Info("Tip: Drag to explore the tree. Scroll to zoom.");
        toasts.Info("Tip: Drag to explore the tree. Scroll to zoom.");

        toasts.Toasts.Should().ContainSingle();
    }

    [Fact]
    public void DifferentMessageOrType_StillShowsSeparately()
    {
        var toasts = new ToastService();

        toasts.Info("Saved");
        toasts.Success("Saved");
        toasts.Info("Saved again");

        toasts.Toasts.Should().HaveCount(3);
    }
}
