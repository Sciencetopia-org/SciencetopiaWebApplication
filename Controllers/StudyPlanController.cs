// StudyPlanController.cs

using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Extensions;
using OpenAI.Interfaces;

namespace Sciencetopia.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudyPlanController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        public StudyPlanController(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }

        [HttpPost]
        public async Task<IActionResult> GenerateStudyPlan([FromBody] StudyPlanRequest request)
        {
            var prompt = $"Generate a study plan based on the topic \"{request.Name}\" and its description \"{request.Description}\".";

            var requestBody = new
            {
                model = "gpt-3.5-turbo",
                prompt = prompt,
                temperature = 0.7,
                max_tokens = 500
            };

            // Create a new HttpRequestMessage
            var httpRequestMessage = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri("https://api.openai.com/v1/chat/completions"),
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            // Add the Authorization header with your API key
            httpRequestMessage.Headers.Add("Authorization", "Bearer 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");

            var response = await _httpClient.SendAsync(httpRequestMessage);

            var responseBody = await response.Content.ReadAsStringAsync();
            var completionResponse = JsonSerializer.Deserialize<OpenAICompletionResponse>(responseBody);

            var studyPlan = completionResponse.Choices[0].Text.Trim();

            return Ok(new { StudyPlan = studyPlan });
        }
    }
}

public class StudyPlanRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class OpenAICompletionResponse
{
    public Choice[]? Choices { get; set; }

    public class Choice
    {
        public string? Text { get; set; }
    }
}
