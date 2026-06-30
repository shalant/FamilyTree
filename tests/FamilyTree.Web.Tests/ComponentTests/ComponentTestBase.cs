using Bunit;
using FamilyTree.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Web.Tests.ComponentTests
{
    public abstract class ComponentTestBase : TestContext
    {
        protected ComponentTestBase()
        {
            Services.AddSingleton<ToastService>();
            Services.AddSingleton<FamilyTreeLayoutEngine>();
            // Add any other services your components need
        }
    }
}