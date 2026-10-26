using Microsoft.JSInterop;

namespace GpsTrainer.Web.Services;

/// <summary>
/// Copies a browser-filled input into the model. Autofill often sets the DOM value
/// and never raises the event Blazor's bind is listening for.
/// </summary>
public static class FieldValues
{
    public static void CopyDom(IJSRuntime js, string id, string current, Action<string> assign)
    {
        var dom = Read(js, id);
        if (!string.IsNullOrEmpty(dom) && dom != current)
            assign(dom);
    }

    public static Task CopyAllAsync(IJSRuntime js, string[] ids, Func<string, string> current, Action<string, string> assign)
    {
        if (js is IJSInProcessRuntime sync)
        {
            try
            {
                var values = sync.Invoke<Dictionary<string, string>>("gpsTrainerFields.read", new object[] { ids });
                if (values is not null)
                {
                    Apply(ids, values, current, assign);
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex) when (Unavailable(ex))
            {
                // The test host implements the in-process runtime but answers InvokeAsync.
            }
        }

        return CopyAllSlow(js, ids, current, assign);
    }

    public static string Read(IJSRuntime js, string id)
    {
        if (js is not IJSInProcessRuntime sync)
            return "";
        try
        {
            return sync.Invoke<string>("gpsTrainerFields.readOne", id) ?? "";
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            return "";
        }
    }

    private static async Task CopyAllSlow(IJSRuntime js, string[] ids, Func<string, string> current, Action<string, string> assign)
    {
        Dictionary<string, string>? values;
        try
        {
            values = await js.InvokeAsync<Dictionary<string, string>>("gpsTrainerFields.read", new object[] { ids });
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            return;
        }

        Apply(ids, values, current, assign);
    }

    private static void Apply(string[] ids, Dictionary<string, string>? values, Func<string, string> current, Action<string, string> assign)
    {
        if (values is null)
            return;
        foreach (var id in ids)
        {
            if (values.TryGetValue(id, out var dom) && !string.IsNullOrEmpty(dom) && dom != current(id))
                assign(id, dom);
        }
    }

    private static bool Unavailable(Exception ex) =>
        ex is JSException or InvalidOperationException || ex.GetType().Name.Contains("JSRuntime", StringComparison.Ordinal);
}
