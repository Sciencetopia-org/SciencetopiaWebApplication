public class UserActivityMiddleware
{
    private readonly RequestDelegate _next;

    public UserActivityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        // 在这里记录用户活动（例如，访问的页面）
        // 将活动数据保存到数据库

        await _next(context);
    }
}