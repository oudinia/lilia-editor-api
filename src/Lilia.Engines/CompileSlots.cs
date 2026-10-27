using System.Collections.Concurrent;

namespace Lilia.Engines;

/// <summary>
/// The machine's compile slots: every TeX and Typst process the API starts waits here for one.
///
/// <para>Before (26 Oct stress test): LaTeX had 3 slots of its own, Typst none, and the compilation
/// queue 5 workers of its own. Fifty people pressing Compile at once waited up to 51 s for those 3
/// slots while the CPU never passed 77%. Now there is one number, sized to the machine
/// (<see cref="DefaultSlots"/>, or <c>Compilation:Slots</c>).</para>
///
/// <para>A web request that calls <see cref="ForRequest"/> (the PDF export, which the preview uses)
/// also gets three things: its place in line is given up when the browser goes away (a newer draft,
/// a closed tab) and its running TeX is stopped; it waits at most <see cref="CompileLine.MaxWait"/>, then
/// <see cref="CompileBusyException"/> (503, try again) rather than running into Cloudflare's 100 s;
/// and its place in line can be asked for by ticket (<see cref="PlaceOf"/>), which the preview shows
/// as "3rd in line". Every other caller waits as long as it takes, as before.</para>
///
/// <para>Waiting is in arrival order: <see cref="SemaphoreSlim"/> serves async waiters first come, first
/// served.</para>
/// </summary>
public static class CompileSlots
{
    /// <summary>Three quarters of the cores, at least 2: room left for the API, the database calls and the web server.</summary>
    public static int DefaultSlots(int cores) => Math.Max(2, cores * 3 / 4);

    /// <summary>This process's line.</summary>
    public static CompileLine Line { get; private set; } = new(DefaultSlots(Environment.ProcessorCount), TimeSpan.FromSeconds(60));

    public static int Slots => Line.Slots;
    public static int WaitingCount => Line.WaitingCount;
    public static int RunningCount => Line.RunningCount;

    internal sealed record Request(CancellationToken Abandoned, string? Ticket);
    private static readonly AsyncLocal<Request?> Ambient = new();
    internal static Request? Current => Ambient.Value;

    /// <summary>Set the size at startup, before any compile. Only an idle line is replaced.</summary>
    public static void Configure(int? slots, int? maxWaitSeconds)
    {
        var size = slots is > 0 ? slots.Value : Line.Slots;
        var wait = maxWaitSeconds is > 0 ? TimeSpan.FromSeconds(maxWaitSeconds.Value) : Line.MaxWait;
        if (size == Line.Slots && wait == Line.MaxWait) return;
        if (Line.RunningCount > 0 || Line.WaitingCount > 0)
            throw new InvalidOperationException("The compile line can only be resized while no compile is running.");
        Line = new CompileLine(size, wait);
    }

    /// <summary>
    /// For the rest of this request's async flow, compiles stop when <paramref name="abandoned"/> fires,
    /// wait at most <see cref="CompileLine.MaxWait"/>, and are known by <paramref name="ticket"/>. Dispose to end it.
    /// </summary>
    public static IDisposable ForRequest(CancellationToken abandoned, string? ticket)
    {
        var previous = Ambient.Value;
        Ambient.Value = new Request(abandoned, ticket);
        return new Restore(previous);
    }

    /// <summary>
    /// Work started from a request that must not end with it: a preamble format built in the
    /// background is used by every later compile, whoever's request started it.
    /// </summary>
    public static void Detach() => Ambient.Value = null;

    /// <summary>The current request's "the browser went away", or none.</summary>
    public static CancellationToken Abandoned => Ambient.Value?.Abandoned ?? CancellationToken.None;

    /// <summary><paramref name="ct"/> when the caller has one, else the request's own.</summary>
    public static CancellationToken With(CancellationToken ct) => ct.CanBeCanceled ? ct : Abandoned;

    /// <inheritdoc cref="CompileLine.EnterAsync"/>
    public static Task<CompileLine.Slot> EnterAsync(CancellationToken ct = default) => Line.EnterAsync(ct);

    /// <inheritdoc cref="CompileLine.PlaceOf"/>
    public static int? PlaceOf(string ticket) => Line.PlaceOf(ticket);

    private sealed class Restore(Request? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

/// <summary>A number of compile slots and the line waiting for them (<see cref="CompileSlots"/>).</summary>
public sealed class CompileLine(int slots, TimeSpan maxWait)
{
    public int Slots { get; } = slots;
    /// <summary>How long a request (<see cref="CompileSlots.ForRequest"/>) waits before it is told to try again.</summary>
    public TimeSpan MaxWait { get; } = maxWait;

    private readonly SemaphoreSlim _gate = new(slots, slots);
    private long _arrivals;
    private int _running;
    /// <summary>Who is waiting, by arrival: the ticket of each (null: a caller without one).</summary>
    private readonly ConcurrentDictionary<long, string?> _waiting = new();
    /// <summary>Tickets holding a slot, with how many (a request can hold one while another of its compiles waits).</summary>
    private readonly ConcurrentDictionary<string, int> _holding = new();

    public int WaitingCount => _waiting.Count;
    public int RunningCount => Volatile.Read(ref _running);

    /// <summary>
    /// Wait for a slot. Dispose the result to give it back. Throws <see cref="OperationCanceledException"/>
    /// when the caller or the request gives up, and <see cref="CompileBusyException"/> when a request
    /// has waited <see cref="MaxWait"/>.
    /// </summary>
    public async Task<Slot> EnterAsync(CancellationToken ct = default)
    {
        var request = CompileSlots.Current;
        using var linked = ct.CanBeCanceled && request is { Abandoned.CanBeCanceled: true }
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, request.Abandoned)
            : null;
        var token = linked?.Token ?? CompileSlots.With(ct);

        var arrival = Interlocked.Increment(ref _arrivals);
        _waiting[arrival] = request?.Ticket;
        bool entered;
        try
        {
            entered = await _gate.WaitAsync(request is null ? Timeout.InfiniteTimeSpan : MaxWait, token);
        }
        finally
        {
            _waiting.TryRemove(arrival, out _);
        }
        if (!entered) throw new CompileBusyException(WaitingCount, RunningCount);

        Interlocked.Increment(ref _running);
        if (request?.Ticket is { } ticket) _holding.AddOrUpdate(ticket, 1, (_, n) => n + 1);
        return new Slot(this, request?.Ticket);
    }

    /// <summary>
    /// Where <paramref name="ticket"/> stands: its place in line (1 = next), 0 while it compiles, null
    /// when it is neither (finished, or never seen).
    /// </summary>
    public int? PlaceOf(string ticket)
    {
        long? mine = null;
        foreach (var (arrival, t) in _waiting)
            if (t == ticket && (mine is null || arrival < mine)) mine = arrival;
        if (mine is { } m) return 1 + _waiting.Keys.Count(a => a < m);
        return _holding.ContainsKey(ticket) ? 0 : null;
    }

    private void Leave(string? ticket)
    {
        if (ticket is not null)
        {
            var left = _holding.AddOrUpdate(ticket, 0, (_, n) => n - 1);
            if (left <= 0) _holding.TryRemove(new KeyValuePair<string, int>(ticket, left));
        }
        Interlocked.Decrement(ref _running);
        _gate.Release();
    }

    public sealed class Slot : IDisposable
    {
        private readonly CompileLine _line;
        private readonly string? _ticket;
        private int _released;

        internal Slot(CompileLine line, string? ticket) { _line = line; _ticket = ticket; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _line.Leave(_ticket);
        }
    }
}

/// <summary>Every slot stayed taken for <see cref="CompileLine.MaxWait"/>: try again in a few seconds.</summary>
public sealed class CompileBusyException(int waiting, int running)
    : Exception($"Lilia is compiling many documents right now ({running} compiling, {waiting} waiting). Trying again in a few seconds.")
{
    public int Waiting { get; } = waiting;
    public int Running { get; } = running;
}
