using KairoServer.Contracts.Customers;
using KairoServer.Domain.Customers;

namespace KairoServer.Application.Customers
{
    internal static class CustomerMapping
    {
        public static CustomerView ToView(this Customer c) =>
            new(c.Id, c.Name, c.Enabled, c.CreatedAt, c.UpdatedAt);
    }
}
