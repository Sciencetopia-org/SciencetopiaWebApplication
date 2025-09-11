using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // for RelationalDatabaseFacadeExtensions (GetDbConnection/GetConnectionString)
using Sciencetopia.Services.L10n;

namespace Sciencetopia.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/l10n")] 
    // Support both Bearer JWT and Identity cookie auth
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + ", Identity.Application", Roles = "administrator")]
    public class L10nMigrationController : ControllerBase
    {
        private readonly L10nMigrationRunner _runner;
        public L10nMigrationController(L10nMigrationRunner runner) { _runner = runner; }

        [AllowAnonymous]
        [HttpGet("ping")]
        public IActionResult Ping() => Ok("ok");

        [HttpGet("diag")]
        public ActionResult<object> Diag([FromServices] IConfiguration cfg, [FromServices] IWebHostEnvironment env, [FromServices] Sciencetopia.Data.ApplicationDbContext db)
        {
            var cs = db.Database.GetDbConnection().ConnectionString;
            return Ok(new
            {
                environment = env.EnvironmentName,
                connectionStringLength = cs?.Length ?? 0,
                connectionStringStartsWith = string.IsNullOrEmpty(cs) ? null : cs.Substring(0, Math.Min(16, cs.Length))
            });
        }

        [HttpPost("migrate")]
        public async Task<ActionResult<L10nMigrationReport>> Migrate([FromQuery] bool dryRun = true)
        {
            var report = await _runner.MigrateFromLegacyAsync(dryRun);
            return Ok(report);
        }
    }
}
