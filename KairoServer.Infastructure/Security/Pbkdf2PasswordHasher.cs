using System;
using System.Security.Cryptography;
using KairoServer.Application.Security;

namespace KairoServer.Infastructure.Security
{
    public sealed class Pbkdf2PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int IterationCount = 210000;

        public string Hash(string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(password);

            Span<byte> salt = stackalloc byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                IterationCount,
                HashAlgorithmName.SHA256,
                KeySize);

            return $"{IterationCount}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public bool Verify(string password, string hashedPassword)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hashedPassword))
                return false;

            var parts = hashedPassword.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0], out var iterations) || iterations <= 0)
                return false;

            try
            {
                var salt = Convert.FromBase64String(parts[1]);
                var expectedHash = Convert.FromBase64String(parts[2]);

                var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
