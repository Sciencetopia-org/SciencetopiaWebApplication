public class SearchService
{
    private readonly IKnowledgeNodeRepository _knowledgeRepo;
    private readonly IResourceRepository _resourceRepo;
    private readonly IGraphRepository _neo4jRepository;

    public SearchService(
        IKnowledgeNodeRepository knowledgeRepo,
        IResourceRepository resourceRepo,
        IGraphRepository neo4jRepository)
    {
        _knowledgeRepo = knowledgeRepo;
        _resourceRepo = resourceRepo;
        _neo4jRepository = neo4jRepository;
    }

    public async Task<List<Resource>> SearchResourcesWithLinkedNodesAsync(string query, int skip, int take)
    {
        // Step 1: SQL - 直接匹配资源
        var directMatches = await _resourceRepo.SearchResourcesByQueryAsync(query);

        // Step 2: SQL - 匹配且审核通过的 KnowledgeNode ID
        var approvedNodes = await _knowledgeRepo.SearchKnowledgeNodesAsync(query, 0, int.MaxValue);
        var approvedNodeIds = approvedNodes
            .Where(n => n.Id.HasValue)
            .Select(n => n.Id!.Value)
            .ToList();

        // Step 3: Neo4j - 获取这些节点关联的资源 ID
        var linkedResourceIds = await _neo4jRepository.GetLinkedResourceIdsAsync(approvedNodeIds);

        // Step 4: SQL - 根据 ID 获取资源信息
        var linkedResources = await _resourceRepo.GetResourcesByIdsAsync(linkedResourceIds);

        // Step 5: 合并、去重、分页
        return directMatches
            .Concat(linkedResources)
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .Skip(skip)
            .Take(take)
            .ToList();
    }
}
