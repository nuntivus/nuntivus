namespace NuntivusServer.Contracts.Portal
{
    public sealed record PortalInvitationRequest(string CustomerId, string Provider, string Subject);
    public sealed record PortalInvitationView(Guid Id, string CustomerId, string Provider, string Subject, DateTimeOffset ExpiresAt, string Token);
    public sealed record PortalIdentity(string Issuer, string Subject, long? CredentialVersion = null);
    public sealed record PortalAccount(Guid Id, string Issuer, string Subject, long CredentialVersion);
    public sealed record PortalMembershipView(Guid AccountId, string CustomerId, string Issuer, string Subject, bool Revoked);
    public sealed record PortalContractView(Guid Id, string CustomerId, bool Enabled, string[] EventTypes)
    {
        public PortalContractView() : this(Guid.Empty, string.Empty, false, []) { }
    }
    public sealed record PortalDeliveryView(Guid Id, Guid ContractId, string CustomerId, string EventType, string Status, int AttemptCount);
    public sealed record PortalCustomerView(string Id, string Name);
    public sealed record PortalAcceptRequest(string Token, string? Password = null);
    public sealed record PortalLoginRequest(string Username, string Password);
}
