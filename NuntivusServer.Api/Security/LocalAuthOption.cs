using System;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace NuntivusServer.Api.Security
{
    public sealed class LocalAuthOption
    {
        public bool Enabled { get; set; }

        public string Issuer { get; set; } = "nuntivus.local";

        public string Audience { get; set; } = "nuntivus.dashboard";

        public string SigningKey { get; set; } = string.Empty;

        public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

        public SymmetricSecurityKey CreateSigningKey()
        {
            var keyBytes = Convert.FromBase64String(SigningKey);
            return new SymmetricSecurityKey(keyBytes);
        }

        public static bool HasValidSigningKey(string? signingKey)
        {
            if (string.IsNullOrWhiteSpace(signingKey))
                return false;

            try
            {
                var keyBytes = Convert.FromBase64String(signingKey);
                return keyBytes.Length >= 32;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
