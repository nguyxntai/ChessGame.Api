using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed class OnlineExceptionFilter(ILogger<OnlineExceptionFilter>? logger = null) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        OnlineException? ex = context.Exception as OnlineException;
        if (ex is null)
        {
            ex = context.Exception switch
            {
                MongoWriteException mongo when mongo.WriteError.Category == ServerErrorCategory.DuplicateKey => new("LobbyReservationConflict"),
                MongoCommandException mongo when mongo.Code == 11000 => new("LobbyReservationConflict"),
                MongoException mongo when mongo.HasErrorLabel("TransientTransactionError") => new("OnlineTemporarilyUnavailable", 503),
                MongoConnectionException => new("OnlineTemporarilyUnavailable", 503),
                TimeoutException => new("OnlineTemporarilyUnavailable", 503),
                _ => null
            };
            logger?.LogError(context.Exception, "Online request failed at {Path}; trace {TraceId}",
                context.HttpContext.Request.Path, context.HttpContext.TraceIdentifier);
        }
        if (ex is null) return;
        context.Result = new ObjectResult(new OnlineError(ex.Code, ex.Code, ex.Version)) { StatusCode = ex.Status };
        context.ExceptionHandled = true;
    }
}
