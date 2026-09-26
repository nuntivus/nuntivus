using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NuntivusServer.Domain.Events
{
    public class EventMetadata : Dictionary<string, string>
    {
        public static EventMetadata Parse(string metadata)
        {
            var eventMetadata = new EventMetadata();
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata);
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    eventMetadata[kvp.Key] = kvp.Value;
                }
            }
            return eventMetadata;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
