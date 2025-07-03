using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;

public interface IKnowledgeNodeRepository
{
    Task<IEnumerable<Guid>> GetAllNodeIdsAsync();
    Task<(string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)?> GetNodeDetailsByIdAsync(Guid id);
    Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetNodesDetailsAsync(IEnumerable<Guid> ids);
    Task<Dictionary<Guid, string>> GetNodesNamesAsync(IEnumerable<Guid> ids);
    Task<List<KnowledgeNode>> SearchKnowledgeNodesAsync(string query, int skip, int take);
    // Dictionary<string, object> CreateNode(string id, string name, string description);
    // bool UpdateNode(string id, string name, string description);
    // bool DeleteNode(string id);
}

public class KnowledgeNodeRepository : IKnowledgeNodeRepository
{
    private readonly ApplicationDbContext _context;

    public KnowledgeNodeRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // 获取所有节点Id，转换为字符串
    public async Task<IEnumerable<Guid>> GetAllNodeIdsAsync()
    {
        return await _context.KnowledgeNodes
                             .Where(node => node.Id != null)
                             .Select(node => node.Id.Value)
                             .ToListAsync();
    }

    public async Task<(string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)?> GetNodeDetailsByIdAsync(Guid id)
    {
        var node = await _context.KnowledgeNodes
            .Where(n => n.Id == id)
            .Select(n => new
            {
                n.Name,
                n.Description,
                CreatedDate = n.CreatedDate,
                UpdatedDate = n.UpdatedDate
            })
            .FirstOrDefaultAsync();

        if (node == null)
        {
            return null;
        }

        return (node.Name ?? string.Empty, node.Description ?? string.Empty,
                node.CreatedDate.HasValue ? node.CreatedDate.Value : default,
                node.UpdatedDate.HasValue ? node.UpdatedDate.Value : default
                );
    }

    // 获取节点详情，投影时将 Guid 转为字符串
    public async Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetNodesDetailsAsync(IEnumerable<Guid> ids)
    {
        var dict = new Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>();
        foreach (var id in ids)
        {
            var details = await GetNodeDetailsByIdAsync(id);
            if (details.HasValue)
            {
                dict[id] = details.Value;
            }
        }
        return dict;
    }

    // 获取节点名称，投影时将 Guid 转为字符串
    public async Task<Dictionary<Guid, string>> GetNodesNamesAsync(IEnumerable<Guid> ids)
    {
        var dict = new Dictionary<Guid, string>();
        foreach (var id in ids)
        {
            var name = await _context.KnowledgeNodes
                .Where(n => n.Id == id)
                .Select(n => n.Name)
                .FirstOrDefaultAsync();

            if (name != null)
            {
                dict[id] = name;
            }
        }
        return dict;
    }

    public async Task<List<KnowledgeNode>> SearchKnowledgeNodesAsync(string query, int skip, int take)
    {
        return await _context.KnowledgeNodes
            .Where(n => EF.Functions.Like(n.Name, $"%{query}%") ||
                         EF.Functions.Like(n.Description, $"%{query}%"))
            .OrderByDescending(n => n.UpdatedDate)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    // 创建知识节点：将传入的 id 转换为 Guid
    // public Dictionary<string, object> CreateNode(string id, string name, string description)
    // {
    //     var node = new KnowledgeNode
    //     {
    //         Id = Guid.Parse(id),
    //         Name = name,
    //         Description = description,
    //         CreatedDate = DateTime.Now,
    //         UpdatedDate = DateTime.Now
    //     };
    //     _context.KnowledgeNodes.Add(node);
    //     _context.SaveChanges();
    //     return new Dictionary<string, object>
    //     {
    //         { "Id", node.Id.ToString() },
    //         { "Name", node.Name },
    //         { "Description", node.Description },
    //         { "CreatedDate", node.CreatedDate },
    //         { "UpdatedDate", node.UpdatedDate }
    //     };
    // }

    // // 更新知识节点：使用 Guid.Parse 查找
    // public bool UpdateNode(string id, string name, string description)
    // {
    //     var node = _context.KnowledgeNodes.Find(Guid.Parse(id));
    //     if (node == null) return false;
    //     node.Name = name;
    //     node.Description = description;
    //     node.UpdatedDate = DateTime.Now;
    //     _context.SaveChanges();
    //     return true;
    // }

    // // 删除知识节点：使用 Guid.Parse 查找
    // public bool DeleteNode(string id)
    // {
    //     var node = _context.KnowledgeNodes.Find(Guid.Parse(id));
    //     if (node == null) return false;
    //     _context.KnowledgeNodes.Remove(node);
    //     _context.SaveChanges();
    //     return true;
    // }
}