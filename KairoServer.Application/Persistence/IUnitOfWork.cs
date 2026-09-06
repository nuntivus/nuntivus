using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Application.Persistence
{
    public interface IUnitOfWork
    {
        IDbConnection? GetConnection();
        IDbTransaction? GetTransaction();

        public bool IsConnectionOpen => GetTransaction() != null;
        public bool IsTransactionOpen => GetTransaction() != null;

        void Begin(bool inTransaction = true);
        void Commit();
        void Rollback();
    }
}
