using KairoServer.Common;
using KairoServer.Domain.Contracts;
using KairoServer.Domain.Events;

namespace KairoServer.Application.Matching
{
    public interface IContractFilterEvaluator
    {
        Result<bool> Evaluate(ContractFilter? filter, Event @event);
    }
}