namespace NuntivusServer.Infastructure.Health
{
    public static class RequiredTablesReadinessDefaults
    {
        public static readonly string[] CoreTables =
        [
            "events",
            "sources",
            "outbox_messages",
            "customers",
            "contracts",
            "contract_event_types",
            "contract_schema_targets",
            "deliveries",
            "delivery_attempts",
            "event_replays",
            "event_definitions",
            "event_schema_versions",
            "contract_filter_rules",
            "contract_delivery_rate_limit_state",
            "contract_delivery_ordering_state",
            "delivery_operation_audits",
            "delivery_replay_jobs",
            "audit_logs",
            "schema_migrations"
        ];
    }
}
