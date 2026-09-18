namespace KairoServer.Application.Security
{
    public sealed record EndpointSecurityContext(
        EndpointDirection Direction,
        string Protocol,
        string ComponentName);
}