using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace SPT_Mod_Korean_Server;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class CultureSyncRouter : StaticRouter
{
    internal const string Route = "/sptmodkorean/culture";

    public CultureSyncRouter(JsonUtil jsonUtil)
        : base(jsonUtil,
        [
            new RouteAction<CultureSyncRequest>(
                Route,
                static (url, request, sessionId, output, cancellationToken) => Handle(request, sessionId))
        ])
    { }

    private static ValueTask<string> Handle(CultureSyncRequest request, MongoId sessionId)
    {
        CultureRegistry.Set(sessionId, request.Culture);
        return new ValueTask<string>("{\"ok\":true}");
    }
}
