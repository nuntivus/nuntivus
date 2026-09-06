using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Domain.Events
{
    public class EventId
    {
        public string Id { get; }

        public EventId(string id)
        {
            if(string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Event ID cannot be null or whitespace.", nameof(id));

            if(id.Length < 3 || id.Length > 36)
                throw new ArgumentException("Event ID must be between 3 and 36 characters long.", nameof(id));

            Id = id.Trim();
        }

        public static EventId NewId()
        {
            return new EventId(Guid.NewGuid().ToString());
        }
    }
}
