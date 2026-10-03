using NuntivusServer.Application.Persistence;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuntivusServer.Infastructure.Persistence.Postgres
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
            try { _transaction?.Rollback(); }
            finally { Close(); }
        }

        public void Commit()
        {
            try { _transaction?.Commit(); }
            finally { Close(); }
        }

        public void Begin(bool inTransaction = true)
        {
            if (_connection != null)
                throw new InvalidOperationException("Unit of work is already open.");
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
            Close();
        }

        private void Close()
        {
            _transaction?.Dispose();
            _transaction = null;
            _connection?.Dispose();
            _connection = null;
        }
    }
}
