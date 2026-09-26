using System.Linq;
using NuntivusServer.Api.Security;
using NuntivusServer.Contracts.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NuntivusServer.Api.Controllers.Admin
{
    [ApiController]
    [Authorize(Policy = AdminAuthorizationPolicies.Viewer)]
    [Route("v1/admin/roles")]
    public sealed class RolesAdminController : ControllerBase
    {
        [HttpGet]
        public IActionResult List()
        {
            var roles = AdminPermissionCatalog
                .GetRoleCatalog()
                .Select(static role => new AdminRoleView(role.Name, role.Description, role.Permissions))
                .ToArray();

            return Ok(new AdminRoleCatalogView(roles));
        }
    }
}