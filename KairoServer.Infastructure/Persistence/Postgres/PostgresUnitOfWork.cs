using KairoServer.Application.Persistence;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public class PostgresUnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;
        private IDbConnection? _connection;
        private IDbTransaction? _transaction;

        public PostgresUnitOfWork(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void Rollback()
        {
            if (_transaction != null)
            {
                _transaction.Rollback();
            }
        }

        public void Commit()
        {
            if (_transaction != null)
            {
                _transaction.Commit();
            }
        }

        public void Begin(bool inTransaction = true)
        {
            _connection = _connectionFactory.CreateConnection();
            _connection.Open();

            if (inTransaction)
            {
                _transaction = _connection.BeginTransaction();
            }
        }

        public IDbConnection? GetConnection()
        {
            return _connection;
        }

        public IDbTransaction? GetTransaction()
        {
            return _transaction;
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _connection?.Dispose();
        }
    }
}
