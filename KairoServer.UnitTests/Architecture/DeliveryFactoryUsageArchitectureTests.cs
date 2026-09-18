using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KairoServer.UnitTests.Architecture
{
    public sealed class DeliveryFactoryUsageArchitectureTests
    {
        [Fact]
        public void Delivery_CreatePending_is_only_called_from_DeliveryFactory()
        {
            var solutionRoot = FindSolutionRoot();
            var files = Directory.EnumerateFiles(solutionRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains("\\.vs\\", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith("KairoServer.Domain\\Deliveries\\Delivery.cs", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith("KairoServer.Domain\\Deliveries\\DeliveryFactory.cs", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith("KairoServer.UnitTests\\Architecture\\DeliveryFactoryUsageArchitectureTests.cs", StringComparison.OrdinalIgnoreCase));

            var violations = new List<string>();
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("Delivery.CreatePending(", StringComparison.Ordinal))
                    continue;

                violations.Add(Path.GetRelativePath(solutionRoot, file));
            }

            Assert.True(
                violations.Count == 0,
                "Direct Delivery.CreatePending usage is forbidden outside DeliveryFactory. Violations: "
                + string.Join(", ", violations));
        }

        private static string FindSolutionRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var hasSolution = directory.EnumerateFiles("*.slnx", SearchOption.TopDirectoryOnly).Any();
                if (hasSolution)
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate solution root from test base directory.");
        }
    }
}
