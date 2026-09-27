using System.Net;
using System.Text.Json;
using BSStore.Application.Common;
using BSStore.Domain.Exceptions;

namespace BSStore.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, response) = exception switch
        {
            NotFoundException nfe =>
                (HttpStatusCode.NotFound, ApiResponse.Fail(nfe.Message, nfe.Code)),

            BusinessRuleException bre =>
                (HttpStatusCode.BadRequest, ApiResponse.Fail(bre.Message, bre.Code)),

            InvalidOrderTransitionException iote =>
                (HttpStatusCode.UnprocessableEntity, ApiResponse.Fail(iote.Message, iote.Code)),

            InsufficientStockException ise =>
                (HttpStatusCode.Conflict, ApiResponse.Fail(ise.Message, ise.Code)),

            UnauthorizedException ue =>
                (HttpStatusCode.Forbidden, ApiResponse.Fail(ue.Message, ue.Code)),

            _ => (HttpStatusCode.InternalServerError,
                  ApiResponse.Fail("حدث خطأ داخلي في الخادم.", "INTERNAL_SERVER_ERROR"))
        };

        // Log full details server-side only
        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        else
            _logger.LogWarning("Business exception [{Code}]: {Message}", response.Code, response.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
