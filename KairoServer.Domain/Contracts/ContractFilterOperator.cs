namespace KairoServer.Domain.Contracts
{
    public enum ContractFilterOperator
    {
        Equals = 1,
        NotEquals = 2,
        GreaterThan = 3,
        GreaterThanOrEqual = 4,
        LessThan = 5,
        LessThanOrEqual = 6,
        Contains = 7,
        StartsWith = 8,
        EndsWith = 9,
        In = 10,
        NotIn = 11,
    }
}