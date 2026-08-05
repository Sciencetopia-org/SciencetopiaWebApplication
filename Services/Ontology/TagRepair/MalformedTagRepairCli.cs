using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Sciencetopia.Services.Ontology.Phase3;

namespace Sciencetopia.Services.Ontology.TagRepair;

public sealed record TagRepairCliValidation(bool IsValid, IReadOnlyList<string> Errors);

public static class MalformedTagRepairCli
{
    public static TagRepairCliValidation ValidateConfiguration(IConfiguration configuration)
    {
        var errors = Phase3CliPreflight.ValidateNeo4jConfiguration(configuration).Errors.ToList();
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add("ConnectionStrings:DefaultConnection is missing or empty.");
        }
        else
        {
            try
            {
                var parsed = new SqlConnectionStringBuilder(connectionString);
                if (string.IsNullOrWhiteSpace(parsed.DataSource))
                    errors.Add("ConnectionStrings:DefaultConnection must include Data Source/Server.");
                if (string.IsNullOrWhiteSpace(parsed.InitialCatalog))
                    errors.Add("ConnectionStrings:DefaultConnection must include Initial Catalog/Database.");
            }
            catch (ArgumentException)
            {
                errors.Add("ConnectionStrings:DefaultConnection is not a valid SQL Server connection string.");
            }
        }
        return new TagRepairCliValidation(errors.Count == 0, errors);
    }

    public static TagRepairCliValidation ValidateApplyRequest(TagRepairApplyRequest request, MalformedTagRepairPlan plan)
    {
        var errors = new List<string>();
        if (!request.ConfirmNeo4jWrite)
            errors.Add("Apply requires --confirm-neo4j-write.");
        if (string.IsNullOrWhiteSpace(request.SuppliedPlanHash))
            errors.Add("Apply requires --plan-hash <sha256>.");
        else if (!string.Equals(request.SuppliedPlanHash, plan.PlanHash, StringComparison.OrdinalIgnoreCase))
            errors.Add("The supplied --plan-hash does not match the plan file.");
        if (!MalformedTagRepairPlanner.HasValidHash(plan))
            errors.Add("The plan file hash is invalid; the plan may have been modified.");
        if (plan.BlockingValidationErrors.Count > 0)
            errors.Add($"The plan has {plan.BlockingValidationErrors.Count} blocking validation error(s).");
        return new TagRepairCliValidation(errors.Count == 0, errors);
    }
}
