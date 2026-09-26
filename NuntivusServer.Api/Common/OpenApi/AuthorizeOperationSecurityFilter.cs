using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NuntivusServer.Api.Common.OpenApi
{
    public sealed class AuthorizeOperationSecurityFilter : IOperationFilter
    {
        private const string BearerScheme = "Bearer";

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
            if (endpointMetadata.OfType<AllowAnonymousAttribute>().Any())
                return;

            if (!endpointMetadata.OfType<AuthorizeAttribute>().Any())
                return;

            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = BearerScheme,
                    },
                }] = new string[] { },
            });
        }
    }
}
