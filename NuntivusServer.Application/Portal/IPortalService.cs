using NuntivusServer.Common;
using NuntivusServer.Contracts.Portal;

namespace NuntivusServer.Application.Portal
{
    public sealed class PortalOptions
    {
        public bool LocalEnabled { get; set; }
        public string LocalIssuer { get; set; } = "nuntivus.portal.local";
        public string LocalAudience { get; set; } = "nuntivus.portal";
        public string SigningKey { get; set; } = string.Empty;
        public bool ClerkEnabled { get; set; }
        public string ClerkAuthority { get; set; } = string.Empty;
        public string ClerkAudience { get; set; } = string.Empty;
        public string[] ClerkAuthorizedParties { get; set; } = [];
    }

    public interface IPortalIdentityProvider
    {
        string Name { get; }
        string Issuer { get; }
        bool Enabled { get; }
        bool SupportsPassword { get; }
    }
    public interface IPortalService
    {
        Task<Result<PortalInvitationView>> InviteAsync(PortalInvitationRequest request, string actor, CancellationToken ct);
        Task<Result<PortalAccount>> AcceptAsync(string token, string? password, PortalIdentity? identity, CancellationToken ct);
        Task<Result<PortalAccount>> LoginAsync(string username, string password, CancellationToken ct);
        Task<Result<PagedResult<PortalMembershipView>>> MembershipsAsync(string customerId, int page, CancellationToken ct);
        Task<Result> RevokeAsync(Guid accountId, string customerId, string actor, CancellationToken ct);
        Task<Result> RevokeInvitationAsync(Guid id, string actor, CancellationToken ct);
        Task<Result<PagedResult<PortalCustomerView>>> CustomersAsync(PortalIdentity identity, int page, CancellationToken ct);
        Task<Result<PagedResult<PortalContractView>>> ContractsAsync(PortalIdentity identity, string customerId, int page, Guid? id, CancellationToken ct);
        Task<Result<PagedResult<PortalDeliveryView>>> DeliveriesAsync(PortalIdentity identity, string customerId, int page, Guid? id, CancellationToken ct);
    }
}
