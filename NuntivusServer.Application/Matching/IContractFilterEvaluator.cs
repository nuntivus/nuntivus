using NuntivusServer.Common;
using NuntivusServer.Domain.Contracts;
using NuntivusServer.Domain.Events;

namespace NuntivusServer.Application.Matching
{
    public interface IContractFilterEvaluator
    {
        Result<bool> Evaluate(ContractFilter? filter, Event @event);
    }
}