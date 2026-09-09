using System.Text.Json.Serialization;

namespace KairoServer.Contracts.CustomerContracts
{
    /// <summary>
    /// Base type for all destination configuration payloads carried on a
    /// <see cref="CreateContractRequest"/>. The concrete derived type
    /// (identified by the <c>"type"</c> JSON discriminator) determines both
    /// the schema of the payload and the effective
    /// <see cref="DestinationType"/> for the contract.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(HttpDestinationConfig), typeDiscriminator: "http")]
    public abstract record DestinationConfig;
}
