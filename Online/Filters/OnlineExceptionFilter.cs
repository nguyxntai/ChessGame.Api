using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChessGame.Api.Online;

public sealed class OnlineExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not OnlineException ex) return;
        context.Result = new ObjectResult(new OnlineError(ex.Code, ex.Code, ex.Version)) { StatusCode = ex.Status };
        context.ExceptionHandled = true;
    }
}
