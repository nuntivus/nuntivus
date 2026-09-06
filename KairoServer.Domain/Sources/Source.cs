using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KairoServer.Domain.Sources
{
    public class Source
    {
        public string Id { get; }
        public string Name { get; }
        public SourceType Type { get; }
        public string Configuration { get; }
        public bool Enabled { get; }

        public Source(
            string id,
            string name,
            SourceType type,
            string configuration,
            bool enabled)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Source ID cannot be null or whitespace.", nameof(id));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Source name cannot be null or whitespace.", nameof(name));

            if(string.IsNullOrWhiteSpace(configuration))
                throw new ArgumentException("Source configuration cannot be null or whitespace.", nameof(configuration));

            Id = id;
            Name = name;
            Type = type;
            Configuration = configuration;
            Enabled = enabled;
        }

    }
}
