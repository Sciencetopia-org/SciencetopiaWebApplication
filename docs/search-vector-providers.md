# Search Vector Providers

`SearchVectorService` supports pluggable embedding providers through `Search:Embedding`.

The default behavior is:

- Use `openai` when an OpenAI API key is configured.
- Use `siliconflow` when `SILICONFLOW_API_KEY` is configured and no explicit provider is set.
- Use `mistral` when `MISTRAL_API_KEY` is configured and no explicit provider is set.
- Fall back to `local` hashing vectors when no remote provider key is available.

## OpenAI

```json
{
  "Search": {
    "Embedding": {
      "Provider": "openai",
      "Model": "text-embedding-3-small",
      "ApiKey": "<api-key>"
    }
  }
}
```

## SiliconFlow

```json
{
  "Search": {
    "Embedding": {
      "Provider": "siliconflow",
      "Model": "BAAI/bge-m3",
      "ApiKey": "<api-key>"
    }
  }
}
```

## Alibaba Cloud Bailian / DashScope

Use this for Qwen/Tongyi embedding models exposed through DashScope's OpenAI-compatible
embedding API.

```json
{
  "Search": {
    "Embedding": {
      "Provider": "dashscope",
      "Endpoint": "https://dashscope.aliyuncs.com/compatible-mode/v1/embeddings",
      "Model": "text-embedding-v4",
      "BatchSize": 10,
      "ApiKey": "<dashscope-api-key>"
    }
  }
}
```

For local development, do not store the API key in `appsettings.Development.json`.
Store it in .NET user-secrets instead:

```powershell
dotnet user-secrets set "Search:Embedding:ApiKey" "<dashscope-api-key>"
```

If your Bailian or ModelScope deployment exposes `Qwen/Qwen3-Embedding-8B` under a
different model name, keep the same provider and endpoint, then replace `Model` with
the exact model id shown in that deployment.

## OpenAI-Compatible Custom Provider

Use this for any service that accepts `POST {Endpoint}` with `{ "model": "...", "input": [...] }`
and returns `data[].embedding`.

```json
{
  "Search": {
    "Embedding": {
      "Provider": "openai-compatible",
      "Endpoint": "https://your-provider.example/v1/embeddings",
      "Model": "your-embedding-model",
      "ApiKey": "<api-key>"
    }
  }
}
```

After changing provider or model, rebuild the vector index:

```http
POST /api/Search/ReindexVectors?limit=64
GET /api/Search/VectorStatus
```

DashScope `text-embedding-v4` accepts at most 10 input texts per embedding request,
so keep `BatchSize` at `10` unless the selected model's documentation allows more.
