using System;
using System.Text.Json;

namespace NuntivusServer.Contracts.CustomerContracts
{
    public sealed record ContractTestEventRequest(
        string Type,
        JsonElement Payload,
        JsonElement? Metadata);

    public sealed record ContractTestPreviewView(
        bool Matches,
        string Reason);

    public sealed record ContractTestSendView(
        Guid EventId,
        Guid DeliveryId);
}
