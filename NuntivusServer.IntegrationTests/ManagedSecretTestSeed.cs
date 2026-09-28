using System;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NuntivusServer.Infastructure.Deliveries.Http;

namespace NuntivusServer.IntegrationTests
{
    internal static class ManagedSecretTestSeed
    {
        public static async Task SeedAsync(NpgsqlConnection connection, params string[] names)
        {
            var protector = new AesGcmDestinationCredentialProtector(new DestinationCredentialOption
            {
                EncryptionKey = Convert.ToBase64String(new byte[32])
            });

            foreach (var name in names)
            {
                await connection.ExecuteAsync("INSERT INTO managed_secrets (name) VALUES (@Name)", new { Name = name });
                await connection.ExecuteAsync(
                    "INSERT INTO managed_secret_versions (name, version, encrypted_value) VALUES (@Name, 1, @Value)",
                    new { Name = name, Value = protector.Protect("integration-test-value").Value });
            }
        }
    }
}
