using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Neo4j.Driver;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;

public class DataSyncService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ElasticsearchClient _elasticClient;
    private readonly IDriver _neo4jDriver;

    public DataSyncService(IHttpClientFactory httpClientFactory, ElasticsearchClient elasticClient, IDriver neo4jDriver)
    {
        _httpClientFactory = httpClientFactory;
        _elasticClient = elasticClient;
        _neo4jDriver = neo4jDriver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncDataFromNeo4jToElasticsearch();
            // Adjust the delay as needed
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken); // Repeat every 24 hours
        }
    }

    private async Task SyncDataFromNeo4jToElasticsearch()
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var cursor = await session.RunAsync("MATCH (n:Resource) RETURN n.link AS link");
            var records = await cursor.ToListAsync();

            foreach (var record in records)
            {
                var link = record["link"].As<string>();
                var httpClient = _httpClientFactory.CreateClient();
                var response = await httpClient.GetStringAsync(link);

                // Assuming ExtractContentFromResponse is a method you define to extract
                // meaningful content from your HTTP response
                var content = ExtractContentFromResponse(response);

                try
                {
                    var indexResponse = await _elasticClient.IndexAsync(new
                    {
                        Link = link,
                        Content = content,
                        Timestamp = DateTime.UtcNow
                    }, idx => idx.Index("your_index_name"));

                    // Here you can check the response status and handle it accordingly
                    if (indexResponse.Result != Elastic.Clients.Elasticsearch.Result.Created && indexResponse.Result != Elastic.Clients.Elasticsearch.Result.Updated)
                    {
                        Console.WriteLine($"Document indexing resulted in {indexResponse.Result}");
                    }
                }
                catch (Exception ex)
                {
                    // Handle any exceptions, such as network issues or Elasticsearch errors
                    Console.WriteLine($"An error occurred while indexing the document: {ex.Message}");
                }
            }
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private string ExtractContentFromResponse(string response)
    {
        // Implement your content extraction logic here
        return response; // Simplified for example purposes
    }
}
