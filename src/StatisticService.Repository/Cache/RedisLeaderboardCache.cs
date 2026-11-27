using System.Globalization;
using StackExchange.Redis;
using StatisticService.Repository.Interfaces;

namespace StatisticService.Repository.Cache;

// SCRUM-195: the weekly leaderboard in Redis. Keys per ISO week (Danish time), e.g. for 2026-W48:
//   stats:{2026-W48}:ready     "1" once the week has been built from Postgres
//   stats:{2026-W48}:building  rebuild lock (token, expires after RebuildLockTime)
//   stats:{2026-W48}:games     set of gameIds already counted (idempotency)
//   stats:{2026-W48}:players   hash userId -> "games|wins|correct|answered|lastEndUnixMs|userName"
//   stats:{2026-W48}:wins      sorted set userId -> wins (only players with a win)
//   stats:{2026-W48}:winrate   sorted set userId -> wins/games (only players with >= minGames games)
//   stats:{2026-W48}:accuracy  sorted set userId -> correct/answered (same players)
// The {week} part is a Redis Cluster hash tag: all keys of a week land on the same shard, so the
// Lua scripts below can touch them together if Redis is ever sharded.
//
// Each change is one Lua script, so it is atomic: a reader never sees half a game, and two pods
// adding the same game at once count it once (SADD on the games set decides).
public sealed class RedisLeaderboardCache(IConnectionMultiplexer redis) : ILeaderboardCache
{
    // Weeks are kept this long after their last change; an older week is rebuilt if someone reads it.
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(56);

    // A rebuild that takes longer than this loses its lock, and its result is thrown away.
    public static readonly TimeSpan RebuildLockTime = TimeSpan.FromSeconds(60);

    private const string Separator = "|";

    public bool Enabled => true;

    // KEYS: ready, building, games, players, wins, winrate, accuracy
    // ARGV: gameId, endUnixMs, ttlSeconds, minGames, then 5 per player: userId, userName, won (0/1), correct, answered
    // Returns 0 applied, 1 already applied, 2 week not cached, 3 rebuilding.
    private const string AddGameScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then return 3 end
        if redis.call('EXISTS', KEYS[1]) == 0 then return 2 end
        if redis.call('SADD', KEYS[3], ARGV[1]) == 0 then return 1 end
        local endMs = tonumber(ARGV[2])
        local minGames = tonumber(ARGV[4])
        for i = 5, #ARGV, 5 do
          local id = ARGV[i]
          local name = ARGV[i + 1]
          local g, w, c, a, t = 0, 0, 0, 0, 0
          local old = redis.call('HGET', KEYS[4], id)
          if old then
            local og, ow, oc, oa, ot, oname = string.match(old, '^(%d+)|(%d+)|(%d+)|(%d+)|(%d+)|(.*)$')
            g, w, c, a, t = tonumber(og), tonumber(ow), tonumber(oc), tonumber(oa), tonumber(ot)
            -- the name from the newest game wins; an older game delivered late keeps the newer name
            if t > endMs then name = oname end
          end
          g = g + 1
          w = w + tonumber(ARGV[i + 2])
          c = c + tonumber(ARGV[i + 3])
          a = a + tonumber(ARGV[i + 4])
          if endMs > t then t = endMs end
          redis.call('HSET', KEYS[4], id, g .. '|' .. w .. '|' .. c .. '|' .. a .. '|' .. string.format('%d', t) .. '|' .. name)
          if w > 0 then redis.call('ZADD', KEYS[5], w, id) end
          if g >= minGames then
            redis.call('ZADD', KEYS[6], w / g, id)
            local acc = 0
            if a > 0 then acc = c / a end
            redis.call('ZADD', KEYS[7], acc, id)
          end
        end
        for _, k in ipairs({1, 3, 4, 5, 6, 7}) do redis.call('EXPIRE', KEYS[k], ARGV[3]) end
        return 0
        """;

    // KEYS: ready, players, wins, winrate, accuracy
    // ARGV: top
    // Returns nil if the week is not ready, else a flat list userId, value, userId, value, ...
    private const string CandidatesScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then return false end
        local top = tonumber(ARGV[1])
        local ids = {}
        for k = 3, 5 do
          local r = redis.call('ZREVRANGE', KEYS[k], 0, top - 1, 'WITHSCORES')
          for i = 1, #r, 2 do ids[r[i]] = true end
          -- a full list: everybody with the same score as the last one may also belong in the top
          if #r == 2 * top then
            for _, id in ipairs(redis.call('ZRANGEBYSCORE', KEYS[k], r[#r], r[#r])) do ids[id] = true end
          end
        end
        local out = {}
        for id, _ in pairs(ids) do
          local v = redis.call('HGET', KEYS[2], id)
          if v then
            out[#out + 1] = id
            out[#out + 1] = v
          end
        end
        return out
        """;

    // KEYS: ready, building, games, players, wins, winrate, accuracy
    // ARGV: token, ttlSeconds, minGames, gameCount, gameIds..., then 7 per player: userId, userName, games, wins, correct, answered, lastEndUnixMs
    // Returns 1 if the week was replaced, 0 if the lock is no longer ours.
    private const string CompleteRebuildScript = """
        if redis.call('GET', KEYS[2]) ~= ARGV[1] then return 0 end
        redis.call('DEL', KEYS[3], KEYS[4], KEYS[5], KEYS[6], KEYS[7])
        local minGames = tonumber(ARGV[3])
        local gameCount = tonumber(ARGV[4])
        for i = 5, 4 + gameCount do redis.call('SADD', KEYS[3], ARGV[i]) end
        for i = 5 + gameCount, #ARGV, 7 do
          local id = ARGV[i]
          local g, w, c, a = tonumber(ARGV[i + 2]), tonumber(ARGV[i + 3]), tonumber(ARGV[i + 4]), tonumber(ARGV[i + 5])
          redis.call('HSET', KEYS[4], id, g .. '|' .. w .. '|' .. c .. '|' .. a .. '|' .. ARGV[i + 6] .. '|' .. ARGV[i + 1])
          if w > 0 then redis.call('ZADD', KEYS[5], w, id) end
          if g >= minGames then
            redis.call('ZADD', KEYS[6], w / g, id)
            local acc = 0
            if a > 0 then acc = c / a end
            redis.call('ZADD', KEYS[7], acc, id)
          end
        end
        redis.call('SET', KEYS[1], '1')
        for _, k in ipairs({1, 3, 4, 5, 6, 7}) do redis.call('EXPIRE', KEYS[k], ARGV[2]) end
        redis.call('DEL', KEYS[2])
        return 1
        """;

    public static string Key(string week, string part) => $"stats:{{{week}}}:{part}";

    private static RedisKey[] AllKeys(string week) =>
    [
        Key(week, "ready"), Key(week, "building"), Key(week, "games"), Key(week, "players"),
        Key(week, "wins"), Key(week, "winrate"), Key(week, "accuracy")
    ];

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    public async Task<CacheWriteResult> AddGameAsync(string week, Guid gameId, long endUnixMs,
        IReadOnlyList<CachedPlayerResult> players, int minGamesForRates, CancellationToken ct = default)
    {
        var args = new List<RedisValue>
        {
            gameId.ToString(), Number(endUnixMs), Number((long)Ttl.TotalSeconds), Number(minGamesForRates)
        };
        foreach (var p in players)
        {
            args.Add(Number(p.UserId));
            args.Add(p.UserName);
            args.Add(p.Won ? "1" : "0");
            args.Add(Number(p.CorrectAnswers));
            args.Add(Number(p.AnsweredRounds));
        }

        var result = (int)await Run(AddGameScript, AllKeys(week), [.. args]);
        return result switch
        {
            0 => CacheWriteResult.Applied,
            1 => CacheWriteResult.AlreadyApplied,
            2 => CacheWriteResult.WeekNotCached,
            _ => CacheWriteResult.Rebuilding
        };
    }

    public async Task<IReadOnlyList<WeekPlayerTotals>?> GetCandidatesAsync(string week, int top, CancellationToken ct = default)
    {
        RedisKey[] keys = [Key(week, "ready"), Key(week, "players"), Key(week, "wins"), Key(week, "winrate"), Key(week, "accuracy")];
        var result = await Run(CandidatesScript, keys, [Number(top)]);
        if (result.IsNull)
            return null;

        var flat = (RedisResult[])result!;
        var players = new List<WeekPlayerTotals>(flat.Length / 2);
        for (var i = 0; i < flat.Length; i += 2)
            players.Add(Parse(int.Parse((string)flat[i]!, CultureInfo.InvariantCulture), (string)flat[i + 1]!));
        return players;
    }

    public async Task<string?> TryStartRebuildAsync(string week, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        var taken = await Call(() => redis.GetDatabase().StringSetAsync(Key(week, "building"), token, RebuildLockTime, When.NotExists));
        return taken ? token : null;
    }

    public async Task<bool> CompleteRebuildAsync(string week, string token, IReadOnlyList<WeekPlayerTotals> players,
        IReadOnlyList<Guid> gameIds, int minGamesForRates, CancellationToken ct = default)
    {
        var args = new List<RedisValue>(4 + gameIds.Count + players.Count * 7)
        {
            token, Number((long)Ttl.TotalSeconds), Number(minGamesForRates), Number(gameIds.Count)
        };
        args.AddRange(gameIds.Select(id => (RedisValue)id.ToString()));
        foreach (var p in players)
        {
            args.Add(Number(p.UserId));
            args.Add(p.UserName);
            args.Add(Number(p.GamesPlayed));
            args.Add(Number(p.Wins));
            args.Add(Number(p.CorrectAnswers));
            args.Add(Number(p.AnsweredRounds));
            args.Add(Number(p.LastEndUnixMs));
        }

        return (int)await Run(CompleteRebuildScript, AllKeys(week), [.. args]) == 1;
    }

    // "games|wins|correct|answered|lastEndUnixMs|userName"; the name is last, so it may contain '|'.
    public static WeekPlayerTotals Parse(int userId, string value)
    {
        var parts = value.Split(Separator, 6);
        return new WeekPlayerTotals(
            userId,
            parts[5],
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            int.Parse(parts[3], CultureInfo.InvariantCulture),
            long.Parse(parts[4], CultureInfo.InvariantCulture));
    }

    private Task<RedisResult> Run(string script, RedisKey[] keys, RedisValue[] args) =>
        Call(() => redis.GetDatabase().ScriptEvaluateAsync(script, keys, args));

    // Every Redis failure becomes CacheUnavailableException, so the layers above never see
    // StackExchange.Redis types. RedisTimeoutException is a TimeoutException, not a RedisException.
    private static async Task<T> Call<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception e) when (e is RedisException or RedisTimeoutException)
        {
            throw new CacheUnavailableException("Redis is not available", e);
        }
    }
}
