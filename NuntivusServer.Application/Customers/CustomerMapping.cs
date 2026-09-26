using NuntivusServer.Contracts.Customers;
using NuntivusServer.Domain.Customers;

namespace NuntivusServer.Application.Customers
{
    internal static class CustomerMapping
    {
        public static CustomerView ToView(this Customer c) =>
            new(c.Id, c.Name, c.Enabled, c.CreatedAt, c.UpdatedAt);
    }
}
