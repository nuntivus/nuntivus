using KairoServer.Domain.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Domain.Sources
{
    public class SourceId
    {
        public string Id { get; }

        public SourceId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Source ID cannot be null or whitespace.", nameof(id));

            if (id.Length < 1 || id.Length > 36)
                throw new ArgumentException("Source ID must be between 1 and 36 characters long.", nameof(id));

            Id = id.Trim();
        }

        public static SourceId NewId()
        {
            return new SourceId(Guid.NewGuid().ToString());
        }

        public string Value() { return Id; }
    }
}
