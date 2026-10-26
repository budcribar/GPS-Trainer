namespace GpsTrainer.Web.Maps;

internal static class MapCallback
{
    public static async Task Raise(Delegate? handlers, params object?[] args)
    {
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList())
        {
            if (handler.DynamicInvoke(args) is Task task)
                await task;
        }
    }
}
