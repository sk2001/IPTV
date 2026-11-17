using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using IptvPlayer.Models;
using IptvPlayer.ViewModels;

namespace IptvPlayer.Services.Remote;

/// <summary>Synchronisation du PC avec un appareil Android appairé : favoris, historique, préférences et (si activé) comptes.</summary>
public static class PcSync
{
    // Les identifiants de compte sont échangés au format GUID « D » (avec tirets), comme sur Android.
    private static string Acc(Guid g) => g.ToString("D");

    public static SyncEngine CreateEngine(MainViewModel vm, TargetStore store) => new(store.DeviceId, new SyncMeta(), new Dictionary<string, ISyncAdapter>
    {
        ["favorites"] = new FavoritesAdapter(vm),
        ["history"] = new HistoryAdapter(vm),
        ["settings"] = new SettingsAdapter(vm),
        ["accounts"] = new AccountsAdapter(vm),
    });

    /// <summary>Retourne (reçus, envoyés, comptes ignorés).</summary>
    public static async Task<(int Received, int Sent, bool AccountsSkipped)> RunAsync(MainViewModel vm, KnownTarget t, TargetStore store)
    {
        var engine = CreateEngine(vm, store);
        using var client = new RemoteClient(t.Host, t.Port, t.Fingerprint, t.Token);
        int received = 0, sent = 0; var skipped = false;
        foreach (var c in engine.Collections.ToList())
        {
            if (c == "accounts" && !vm.State.SyncAccounts) continue;
            var (code, text) = await client.CallRawAsync(HttpMethod.Get, $"/sync/{c}");
            if (code == 403) { skipped = true; continue; }
            if (code != 200) throw new IOException($"HTTP {code}");
            var remote = SyncEngine.FromJson(text);
            received += engine.Merge(c, remote);
            var push = engine.MissingOn(c, remote.ToDictionary(r => r.Id));
            if (push.Count > 0)
            {
                var (pc, _) = await client.CallRawAsync(HttpMethod.Put, $"/sync/{c}", SyncEngine.ToJson(push));
                if (pc != 200) throw new IOException($"HTTP {pc}");
                sent += push.Count;
            }
        }
        vm.AfterSync();
        return (received, sent, skipped);
    }

    private static string FavToCanon(string key) { var p = key.Split('|', 2); return Guid.TryParseExact(p[0], "N", out var g) ? Acc(g) + "|" + p[1] : key; }
    private static string FavFromCanon(string key) { var p = key.Split('|', 2); return Guid.TryParse(p[0], out var g) ? g.ToString("N") + "|" + p[1] : key; }

    private sealed class FavoritesAdapter(MainViewModel vm) : ISyncAdapter
    {
        public Dictionary<string, JsonObject> Current() => vm.State.Favorites.ToDictionary(FavToCanon, _ => new JsonObject());
        public void Apply(Dictionary<string, JsonObject> upserts, ISet<string> deletes)
        {
            foreach (var k in deletes) vm.State.Favorites.Remove(FavFromCanon(k));
            foreach (var k in upserts.Keys) vm.State.Favorites.Add(FavFromCanon(k));
            StateStore.Save(vm.State);
        }
    }

    private sealed class HistoryAdapter(MainViewModel vm) : ISyncAdapter
    {
        private static string Id(WatchEntry e) => $"{Acc(e.AccountId)}|{(int)e.Kind}|{e.ItemId}|{e.EpisodeId}";
        private static long Ms(DateTime d) => new DateTimeOffset(d.ToUniversalTime()).ToUnixTimeMilliseconds();

        public Dictionary<string, JsonObject> Current() => vm.History.Snapshot().ToDictionary(Id, e =>
        {
            var o = new JsonObject
            {
                ["accountId"] = Acc(e.AccountId), ["kind"] = (int)e.Kind, ["itemId"] = e.ItemId, ["season"] = e.Season, ["number"] = e.Number,
                ["name"] = e.Name, ["positionMs"] = e.PositionMs, ["durationMs"] = e.DurationMs, ["completed"] = e.Completed, ["lastWatched"] = Ms(e.LastWatched),
            };
            if (e.EpisodeId != null) o["episodeId"] = e.EpisodeId;
            if (e.Logo != null) o["logo"] = e.Logo;
            return o;
        });

        public long? Stamp(string id, JsonObject data) => data["lastWatched"]?.GetValue<long>();

        public void Apply(Dictionary<string, JsonObject> upserts, ISet<string> deletes)
        {
            var del = deletes.Select(d => { var p = d.Split('|'); return WatchEntry.MakeKey(Guid.Parse(p[0]), (ContentKind)int.Parse(p[1]), p[2], p.Length > 3 && p[3].Length > 0 ? p[3] : null); }).ToList();
            var ups = upserts.Values.Select(o => new WatchEntry
            {
                AccountId = Guid.Parse(o["accountId"]!.GetValue<string>()), Kind = (ContentKind)(o["kind"]?.GetValue<int>() ?? 0), ItemId = o["itemId"]!.GetValue<string>(),
                EpisodeId = o["episodeId"]?.GetValue<string>(), Season = o["season"]?.GetValue<int>() ?? 0, Number = o["number"]?.GetValue<int>() ?? 0,
                Name = o["name"]?.GetValue<string>() ?? "", Logo = o["logo"]?.GetValue<string>(), PositionMs = o["positionMs"]?.GetValue<long>() ?? 0,
                DurationMs = o["durationMs"]?.GetValue<long>() ?? 0, Completed = o["completed"]?.GetValue<bool>() ?? false,
                LastWatched = DateTimeOffset.FromUnixTimeMilliseconds(o["lastWatched"]?.GetValue<long>() ?? 0).LocalDateTime,
            }).ToList();
            vm.History.ApplySync(ups, del);
        }
    }

    private sealed class SettingsAdapter(MainViewModel vm) : ISyncAdapter
    {
        private static JsonObject V(string s) => new() { ["value"] = s };
        public Dictionary<string, JsonObject> Current() => new()
        {
            ["audioLangVod"] = V(vm.State.AudioLangVod), ["audioLangLive"] = V(vm.State.AudioLangLive), ["subLangVod"] = V(vm.State.SubLangVod),
            ["resumePlayback"] = V(vm.State.ResumePlayback ? "true" : "false"),
        };
        public void Apply(Dictionary<string, JsonObject> upserts, ISet<string> deletes)
        {
            string? Get(string k) => upserts.TryGetValue(k, out var o) ? o["value"]?.GetValue<string>() : null;
            vm.State.AudioLangVod = Get("audioLangVod") ?? vm.State.AudioLangVod;
            vm.State.AudioLangLive = Get("audioLangLive") ?? vm.State.AudioLangLive;
            vm.State.SubLangVod = Get("subLangVod") ?? vm.State.SubLangVod;
            if (Get("resumePlayback") is { } r) vm.State.ResumePlayback = r == "true";
            StateStore.Save(vm.State);
        }
    }

    private sealed class AccountsAdapter(MainViewModel vm) : ISyncAdapter
    {
        public Dictionary<string, JsonObject> Current() => vm.Accounts.ToDictionary(a => Acc(a.Id), a => new JsonObject
        {
            ["name"] = a.Name, ["type"] = a.Type == AccountType.M3u ? "m3u" : "xtream", ["url"] = a.Url, ["username"] = a.Username, ["password"] = a.Password,
        });

        public void Apply(Dictionary<string, JsonObject> upserts, ISet<string> deletes)
        {
            var modified = new HashSet<Guid>();
            foreach (var d in deletes)
                if (Guid.TryParse(d, out var g) && vm.Accounts.FirstOrDefault(a => a.Id == g) is { } x) { vm.Accounts.Remove(x); modified.Add(g); }
            foreach (var (id, o) in upserts)
            {
                if (!Guid.TryParse(id, out var g)) continue;
                var a = vm.Accounts.FirstOrDefault(x => x.Id == g);
                var isNew = a == null;
                a ??= new Account { Id = g };
                a.Name = o["name"]?.GetValue<string>() ?? ""; a.Type = o["type"]?.GetValue<string>() == "m3u" ? AccountType.M3u : AccountType.Xtream;
                a.Url = o["url"]?.GetValue<string>() ?? ""; a.Username = o["username"]?.GetValue<string>() ?? ""; a.Password = o["password"]?.GetValue<string>() ?? "";
                if (isNew) vm.Accounts.Add(a);
                modified.Add(g);
            }
            vm.AccountsEdited(modified);
        }
    }
}
