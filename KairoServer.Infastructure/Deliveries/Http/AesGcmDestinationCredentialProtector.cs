using System;
using System.Security.Cryptography;
using System.Text;
using KairoServer.Application.Contracts.Destinations;
using KairoServer.Common;

namespace KairoServer.Infastructure.Deliveries.Http
{
    public sealed class AesGcmDestinationCredentialProtector : IDestinationCredentialProtector
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int RequiredKeyBytes = 32;
        private readonly byte[] _key;

        public AesGcmDestinationCredentialProtector(DestinationCredentialOption? option = null)
        {
            var configuredKey = option?.EncryptionKey;
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                configuredKey = Environment.GetEnvironmentVariable("KAIRO_DESTINATION_CREDENTIALS_KEY");
            }

            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                throw new InvalidOperationException(
                    "Destination credential encryption key is required. Configure DestinationCredentials:EncryptionKey or KAIRO_DESTINATION_CREDENTIALS_KEY with a base64-encoded 32-byte key.");
            }

            try
            {
                _key = Convert.FromBase64String(configuredKey);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Destination credential encryption key must be a valid base64 string.",
                    ex);
            }

            if (_key.Length != RequiredKeyBytes)
            {
                throw new InvalidOperationException(
                    "Destination credential encryption key must decode to exactly 32 bytes.");
            }
        }

        public Result<string> Protect(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Result.Failure<string>(Error.Validation("Credential value cannot be empty."));

            try
            {
                var plaintext = Encoding.UTF8.GetBytes(value);
                var nonce = RandomNumberGenerator.GetBytes(NonceSize);
                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[TagSize];

                using var aes = new AesGcm(_key, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag);

                var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
                Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
                Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
                Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

                return Result.Success($"v1.{Convert.ToHexString(payload).ToLowerInvariant()}");
            }
            catch (Exception ex)
            {
                return Result.Failure<string>(ex);
            }
        }

        public Result<string> Unprotect(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Result.Failure<string>(Error.Validation("Protected credential payload is required."));

            var parts = value.Split('.', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !parts[0].Equals("v1", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<string>(Error.Validation("Unsupported protected credential format."));
            }

            try
            {
                var payload = Convert.FromHexString(parts[1]);
                if (payload.Length <= NonceSize + TagSize)
                {
                    return Result.Failure<string>(Error.Validation("Protected credential payload is invalid."));
                }

                var nonce = payload.AsSpan(0, NonceSize).ToArray();
                var tag = payload.AsSpan(NonceSize, TagSize).ToArray();
                var ciphertext = payload.AsSpan(NonceSize + TagSize).ToArray();
                var plaintext = new byte[ciphertext.Length];

                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);

                return Result.Success(Encoding.UTF8.GetString(plaintext));
            }
            catch (Exception)
            {
                return Result.Failure<string>(Error.Validation("Protected credential payload could not be decrypted."));
            }
        }
    }
}
