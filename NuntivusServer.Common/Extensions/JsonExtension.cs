using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NuntivusServer.Common.Extensions
{
    public static class JsonExtensions
    {
        public static string? EnsureJsonObject(this JsonElement? json)
        {
            return json switch
            {
                null => "{}",
                { ValueKind: JsonValueKind.Object } m => m.GetRawText(),
                { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => "{}",
                _ => "{}"
            };
        }

        public static string EnsureJsonObject(this JsonElement json)
        {
            return json switch
            {
                { ValueKind: JsonValueKind.Object } m => m.GetRawText(),
                { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => "{}",
                _ => "{}"
            };
        }
    }
}
