using KairoServer.Contracts.Events;
using KairoServer.Domain.Events;

using ContractCompatibilityMode = KairoServer.Contracts.Events.SchemaCompatibilityMode;
using ContractSchemaState = KairoServer.Contracts.Events.EventSchemaState;
using DomainCompatibilityMode = KairoServer.Domain.Events.SchemaCompatibilityMode;
using DomainSchemaState = KairoServer.Domain.Events.EventSchemaState;

namespace KairoServer.Application.Events
{
    internal static class EventDefinitionMapping
    {
        public static EventDefinitionView ToView(this EventDefinition definition) =>
            new(
                definition.EventType,
                definition.DisplayName,
                definition.Description,
                definition.CreatedAt);

        public static EventDefinitionCreated ToCreated(this EventDefinition definition) =>
            new(
                definition.EventType,
                definition.DisplayName,
                definition.Description,
                definition.CreatedAt);

        public static EventSchemaVersionView ToView(this EventSchemaVersion schemaVersion) =>
            new(
                schemaVersion.EventType,
                schemaVersion.Version,
                schemaVersion.Schema,
                MapCompatibilityMode(schemaVersion.CompatibilityMode),
                MapSchemaState(schemaVersion.State),
                schemaVersion.CreatedAt,
                schemaVersion.ActivatedAt,
                schemaVersion.DeprecatedAt);

        public static ContractCompatibilityMode MapCompatibilityMode(DomainCompatibilityMode mode) => mode switch
        {
            DomainCompatibilityMode.Backward => ContractCompatibilityMode.Backward,
            DomainCompatibilityMode.Forward => ContractCompatibilityMode.Forward,
            _ => (ContractCompatibilityMode)(int)mode
        };

        public static DomainCompatibilityMode MapCompatibilityMode(ContractCompatibilityMode mode) => mode switch
        {
            ContractCompatibilityMode.Backward => DomainCompatibilityMode.Backward,
            ContractCompatibilityMode.Forward => DomainCompatibilityMode.Forward,
            _ => (DomainCompatibilityMode)(int)mode
        };

        public static ContractSchemaState MapSchemaState(DomainSchemaState state) => state switch
        {
            DomainSchemaState.Draft => ContractSchemaState.Draft,
            DomainSchemaState.Active => ContractSchemaState.Active,
            DomainSchemaState.Deprecated => ContractSchemaState.Deprecated,
            _ => (ContractSchemaState)(int)state
        };
    }
}