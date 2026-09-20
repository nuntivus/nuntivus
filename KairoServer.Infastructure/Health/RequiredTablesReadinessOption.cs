namespace KairoServer.Infastructure.Health
{
    public sealed class RequiredTablesReadinessOption
    {
        public string Schema { get; set; } = "public";

        public string[] Tables { get; set; } = [];
    }
}
