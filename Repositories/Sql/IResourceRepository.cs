using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;

public interface IResourceRepository
{
    Task<List<Resource>> SearchResourcesByQueryAsync(string query);
    Task<List<Resource>> GetResourcesByIdsAsync(IEnumerable<string> ids);
    Task<string> EnsureResourceExistsAsync(string name, string link);
}

public class ResourceRepository : IResourceRepository
{
    private readonly ApplicationDbContext _context;

    public ResourceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Resource>> SearchResourcesByQueryAsync(string query)
    {
        return await _context.Resources
            .Where(r => EF.Functions.Like(r.Name, $"%{query}%") ||
                        EF.Functions.Like(r.Link, $"%{query}%"))
            .ToListAsync();
    }

    public async Task<List<Resource>> GetResourcesByIdsAsync(IEnumerable<string> ids)
    {
        return await _context.Resources
            .Where(r => ids.Contains(r.Id.ToString()!))
            .ToListAsync();
    }

    public async Task<string> EnsureResourceExistsAsync(string name, string link)
    {
        var existing = await _context.Resources
            .FirstOrDefaultAsync(r => r.Link == link);

        if (existing != null)
            return existing.Id.ToString()!;

        var newResource = new Resource
        {
            Id = Guid.NewGuid(),
            Name = name,
            Link = link,
        };

        _context.Resources.Add(newResource);
        await _context.SaveChangesAsync();
        return newResource.Id.ToString()!;
    }
}
