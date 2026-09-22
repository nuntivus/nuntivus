using System.Linq;
using KairoServer.Api.Security;
using KairoServer.Contracts.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KairoServer.Api.Controllers.Admin
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