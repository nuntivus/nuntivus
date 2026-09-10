using System.Linq;
using System.Reflection;
using KairoServer.Application.Deliveries.Execution;
using KairoServer.Infastructure.Deliveries.Http;
using NetArchTest.Rules;

namespace KairoServer.UnitTests.Architecture
{
    /// <summary>
    /// Enforces the Delivery Core ↔ destination-implementation boundary:
    /// Application execution types must not depend on HTTP or Infrastructure,
    /// and every <see cref="IDeliveryExecutor"/> implementation lives in
    /// <c>KairoServer.Infastructure.Deliveries</c>.
    /// </summary>
    public class DeliveryBoundaryArchitectureTests
    {
        private static readonly Assembly ApplicationAssembly = typeof(IDeliveryExecutor).Assembly;
        private static readonly Assembly InfrastructureAssembly = typeof(HttpDeliveryExecutor).Assembly;

        [Fact]
        public void Application_execution_types_do_not_depend_on_HttpClient_or_Infrastructure()
        {
            var result = Types.InAssembly(ApplicationAssembly)
                .That().ResideInNamespace("KairoServer.Application.Deliveries.Execution")
                .ShouldNot()
                .HaveDependencyOnAny(
                    "System.Net.Http",
                    "KairoServer.Infastructure",
                    "KairoServer.Contracts.CustomerContracts")
                .GetResult();

            Assert.True(result.IsSuccessful,
                "Delivery execution boundary leaked to HTTP/Infra: "
                + string.Join(", ", result.FailingTypeNames ?? System.Array.Empty<string>()));
        }

        [Fact]
        public void Application_assembly_does_not_reference_Infrastructure()
        {
            var referenced = ApplicationAssembly.GetReferencedAssemblies()
                .Any(a => a.Name == "KairoServer.Infastructure");

            Assert.False(referenced, "KairoServer.Application must not reference KairoServer.Infastructure.");
        }

        [Fact]
        public void All_IDeliveryExecutor_implementations_live_in_Infrastructure_Deliveries()
        {
            var executorTypes = InfrastructureAssembly.GetTypes()
                .Where(t => typeof(IDeliveryExecutor).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            Assert.NotEmpty(executorTypes);
            foreach (var t in executorTypes)
            {
                Assert.StartsWith("KairoServer.Infastructure.Deliveries",
                    t.Namespace ?? string.Empty);
            }
        }
    }
}
