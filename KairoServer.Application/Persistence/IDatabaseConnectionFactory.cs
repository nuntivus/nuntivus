using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Application.Persistence
{
    public interface IDatabaseConnectionFactory
    {
        IDbConnection CreateConnection();
    }
}
