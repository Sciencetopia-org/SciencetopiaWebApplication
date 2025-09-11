using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Sciencetopia.Middleware
{
    public interface ILanguageContext
    {
        string? EffectiveLang { get; }
    }

    public class LanguageContext : ILanguageContext
    {
        private readonly IHttpContextAccessor _http;
        public LanguageContext(IHttpContextAccessor http) { _http = http; }
        public string? EffectiveLang => _http.HttpContext?.Items["effectiveLang"] as string;
    }

    public class LanguageResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _defaultLang;
        public LanguageResolutionMiddleware(RequestDelegate next, IConfiguration cfg)
        {
            _next = next;
            _defaultLang = cfg["L10n:DefaultLang"] ?? "en";
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string? lang = context.Request.Query["lang"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(lang))
                lang = context.Request.Headers["X-Lang"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(lang))
            {
                var accept = context.Request.Headers["Accept-Language"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(accept))
                {
                    lang = accept.Split(',').FirstOrDefault()?.Trim();
                }
            }
            if (string.IsNullOrWhiteSpace(lang))
                lang = _defaultLang;

            // normalize: e.g., en-US to en-US
            try { lang = CultureInfo.GetCultureInfo(lang!).Name; } catch { }
            context.Items["effectiveLang"] = lang;
            await _next(context);
        }
    }
}

