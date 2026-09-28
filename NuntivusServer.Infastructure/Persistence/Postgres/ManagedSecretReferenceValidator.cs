using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NuntivusServer.Application.Persistence;
using NuntivusServer.Common;

namespace NuntivusServer.Infastructure.Persistence.Postgres
{
    internal static class ManagedSecretReferenceValidator
    {
        public static async Task<Result> ValidateAndLockAsync(
            string configurationJson,
            string? authenticationReference,
            IUnitOfWork uow,
            CancellationToken cancellationToken)
        {
            var references = new HashSet<string>(StringComparer.Ordinal);
            using var configuration = JsonDocument.Parse(configurationJson);
            var collection = Collect(configuration.RootElement, references);
            if (collection.IsFailure)
                return collection;

            if (authenticationReference is not null)
            {
                var authentication = Collect(authenticationReference, references);
                if (authentication.IsFailure)
                    return authentication;
            }

            var connection = uow.GetConnection()!;
            var transaction = uow.GetTransaction()!;
            foreach (var reference in references.OrderBy(name => name, StringComparer.Ordinal))
            {
                var found = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "SELECT 1 FROM managed_secrets WHERE name = @Name FOR KEY SHARE",
                    new { Name = reference }, transaction, cancellationToken: cancellationToken));
                if (found is null)
                    return Result.Failure(Error.Validation($"Secret reference '{reference}' does not exist."));
            }

            return Result.Success();
        }

        private static Result Collect(JsonElement element, HashSet<string> references)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var result = Collect(property.Value, references);
                        if (result.IsFailure)
                            return result;
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        var result = Collect(item, references);
                        if (result.IsFailure)
                            return result;
                    }
                    break;
                case JsonValueKind.String:
                    return Collect(element.GetString()!, references);
            }

            return Result.Success();
        }

        private static Result Collect(string value, HashSet<string> references)
        {
            if (!value.StartsWith("secret.", StringComparison.Ordinal))
                return Result.Success();

            if (!ConfigReference.TryParse(value, "secret", out _))
                return Result.Failure(Error.Validation($"Secret reference '{value}' is invalid."));

            references.Add(value);
            return Result.Success();
        }
    }
}
