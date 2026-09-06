using KairoServer.Application.Persistence;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Infastructure.Persistence.Postgres
{
    public sealed class PostgresDatabaseConnectionFactory : IDatabaseConnectionFactory
    {
        private readonly string _connectionString;

        public PostgresDatabaseConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection CreateConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }
    }
}
