using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractTestEventRequest(
        string Type,
        JsonElement Payload,
        JsonElement? Metadata);

    public sealed record ContractTestPreviewView(
        bool Matches,
        string Reason,
        JsonElement? TransformedPayload = null,
        string? TransformationIdentity = null,
        int? OutputSchemaVersion = null);

    public sealed record ContractTestSendView(
        Guid EventId,
        Guid DeliveryId,
        IReadOnlyCollection<Guid>? DeliveryIds = null);
}
