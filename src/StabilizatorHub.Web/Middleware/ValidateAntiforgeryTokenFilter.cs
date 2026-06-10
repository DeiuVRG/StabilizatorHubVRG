using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StabilizatorHub.Web.Middleware;

/// <summary>
/// CSRF guard for every state-changing controller action: requests other than
/// GET/HEAD/OPTIONS/TRACE must carry the X-XSRF-TOKEN header matching the
/// antiforgery cookie. Actions can opt out with [IgnoreAntiforgeryToken].
/// </summary>
public sealed class ValidateAntiforgeryTokenFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        if (context.ActionDescriptor.EndpointMetadata.OfType<IgnoreAntiforgeryTokenAttribute>().Any())
        {
            return;
        }

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new { error = "Invalid or missing CSRF token. Reload the page." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }
    }
}
