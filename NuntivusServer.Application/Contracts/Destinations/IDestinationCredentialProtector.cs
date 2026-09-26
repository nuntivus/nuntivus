using NuntivusServer.Common;

namespace NuntivusServer.Application.Contracts.Destinations
{
    /// <summary>
    /// Protects destination credentials before persistence and restores them for runtime use.
    /// </summary>
    public interface IDestinationCredentialProtector
    {
        Result<string> Protect(string value);
        Result<string> Unprotect(string value);
    }
}
