using System;
using System.Threading;
using System.Threading.Tasks;
using NuntivusServer.Common;
using NuntivusServer.Contracts.CustomerContracts;

namespace NuntivusServer.Application.Matching
{
    public interface IContractTestEventService
    {
        Task<Result<ContractTestPreviewView>> PreviewAsync(
            Guid contractId,
            ContractTestEventRequest request,
            CancellationToken cancellationToken);

        Task<Result<ContractTestSendView>> SendAsync(
            Guid contractId,
            ContractTestEventRequest request,
            string actorId,
            CancellationToken cancellationToken);
    }
}
