using Lapsus.Core.Licensing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Licensing.AppStore;

// Talks to StoreKit 2 through libLapsusStoreKit.dylib (native/LapsusStoreKit/StoreBridge.swift),
// which package-mas.sh puts next to the executable. Each call hands the bridge a token; the bridge
// answers on a StoreKit thread with that token and a JSON object, which completes the call's task.
[SupportedOSPlatform("macos")]
internal sealed class MacStoreKitPurchases : IStorePurchases
{
    private const string Bridge = "LapsusStoreKit";

    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> Pending = new();

    private static long _nextToken;

    // Kept in a static field: the bridge holds the pointer for the life of the process.
    private static readonly ReplyCallback Reply = OnReply;

    private static MacStoreKitPurchases? _watcher;

    private MacStoreKitPurchases()
    {
    }

    public bool IsAvailable => true;

    public event EventHandler? OwnedChanged;

    // Null when the bridge is missing, which only a broken package does; the app then runs as if
    // nothing were owned rather than failing to start.
    public static MacStoreKitPurchases? TryCreate()
    {
        try
        {
            if (!NativeLibrary.TryLoad(Bridge, typeof(MacStoreKitPurchases).Assembly, null, out _))
                return null;

            var purchases = new MacStoreKitPurchases();
            _watcher = purchases;
            lapsus_store_watch(Reply);
            return purchases;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public async Task<StoreEntitlement> OwnedAsync()
    {
        var answer = await CallAsync(token => lapsus_store_owned(token, Reply)).ConfigureAwait(false);
        return ReadOwned(answer);
    }

    public async Task<IReadOnlyList<StoreProduct>> ProductsAsync()
    {
        var ids = string.Join(",", StoreProducts.All);
        var answer = await CallAsync(token => lapsus_store_products(token, ids, Reply)).ConfigureAwait(false);
        if (!answer.TryGetProperty("products", out var products))
            return [];

        var list = new List<StoreProduct>();
        foreach (var product in products.EnumerateArray())
        {
            if (product.TryGetProperty("id", out var id) && product.TryGetProperty("price", out var price))
                list.Add(new StoreProduct(id.GetString() ?? string.Empty, price.GetString() ?? string.Empty));
        }

        return list;
    }

    public async Task<StorePurchaseResult> PurchaseAsync(string productId)
    {
        var answer = await CallAsync(token => lapsus_store_purchase(token, productId, Reply)).ConfigureAwait(false);
        var status = answer.TryGetProperty("status", out var value) ? value.GetString() : null;
        var error = answer.TryGetProperty("error", out var message) ? message.GetString() : null;
        return status switch
        {
            "purchased" => new StorePurchaseResult(StorePurchaseStatus.Purchased),
            "cancelled" => new StorePurchaseResult(StorePurchaseStatus.Cancelled),
            "pending" => new StorePurchaseResult(StorePurchaseStatus.Pending),
            "unavailable" => new StorePurchaseResult(StorePurchaseStatus.Unavailable),
            _ => new StorePurchaseResult(StorePurchaseStatus.Failed, error)
        };
    }

    public async Task<StoreEntitlement> RestoreAsync()
    {
        var answer = await CallAsync(token => lapsus_store_restore(token, Reply)).ConfigureAwait(false);
        if (answer.TryGetProperty("error", out var error))
            throw new InvalidOperationException(error.GetString());

        return ReadOwned(answer);
    }

    private static StoreEntitlement ReadOwned(JsonElement answer)
    {
        if (!answer.TryGetProperty("owned", out var owned))
            return StoreEntitlement.None;

        var entitlements = new List<StoreEntitlement>();
        foreach (var item in owned.EnumerateArray())
        {
            if (!item.TryGetProperty("productId", out var id))
                continue;

            DateTimeOffset? expires = item.TryGetProperty("expires", out var seconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds.GetDouble() * 1000))
                : null;
            entitlements.Add(new StoreEntitlement(StoreAccessPolicy.PlanOf(id.GetString() ?? string.Empty), expires));
        }

        return StoreAccessPolicy.Strongest(entitlements);
    }

    private static async Task<JsonElement> CallAsync(Action<long> start)
    {
        var token = Interlocked.Increment(ref _nextToken);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[token] = completion;
        try
        {
            start(token);
            // A purchase waits on the user, so the timeout is generous; it only stops a call the
            // bridge never answers from hanging forever.
            return await completion.Task.WaitAsync(CallTimeout).ConfigureAwait(false);
        }
        finally
        {
            Pending.TryRemove(token, out _);
        }
    }

    // Runs on a StoreKit thread. An exception must not cross back into Swift.
    private static void OnReply(long token, IntPtr json)
    {
        try
        {
            if (token == 0)
            {
                var watcher = _watcher;
                watcher?.OwnedChanged?.Invoke(watcher, EventArgs.Empty);
                return;
            }

            if (!Pending.TryGetValue(token, out var completion))
                return;

            var text = Marshal.PtrToStringUTF8(json) ?? "{}";
            using var document = JsonDocument.Parse(text);
            completion.TrySetResult(document.RootElement.Clone());
        }
        catch (Exception ex)
        {
            CrashLog.Log(ex, "StoreKit reply");
            if (Pending.TryGetValue(token, out var completion))
                completion.TrySetException(ex);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReplyCallback(long token, IntPtr json);

    [DllImport(Bridge)]
    private static extern void lapsus_store_owned(long token, ReplyCallback callback);

    [DllImport(Bridge)]
    private static extern void lapsus_store_products(
        long token, [MarshalAs(UnmanagedType.LPUTF8Str)] string ids, ReplyCallback callback);

    [DllImport(Bridge)]
    private static extern void lapsus_store_purchase(
        long token, [MarshalAs(UnmanagedType.LPUTF8Str)] string productId, ReplyCallback callback);

    [DllImport(Bridge)]
    private static extern void lapsus_store_restore(long token, ReplyCallback callback);

    [DllImport(Bridge)]
    private static extern void lapsus_store_watch(ReplyCallback callback);
}
