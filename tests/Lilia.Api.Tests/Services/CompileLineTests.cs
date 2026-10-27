using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// One line for every compile the machine runs (CompileSlots, 27 Oct). The 26 Oct stress test: 50
/// people pressing Compile at once waited up to 51 s for LaTeX's 3 slots while the CPU never passed
/// 77%, Typst had no limit at all, and a preview the browser had dropped kept its slot to the end.
/// Each test has its own line, so the API's real compiles in other tests are never held up.
/// </summary>
public class CompileLineTests
{
    [Theory]
    [InlineData(8, 6)]
    [InlineData(4, 3)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]
    public void The_default_is_three_quarters_of_the_cores_and_never_fewer_than_two(int cores, int slots) =>
        CompileSlots.DefaultSlots(cores).Should().Be(slots);

    [Fact]
    public async Task A_request_knows_its_place_in_line_and_moves_up_as_slots_free()
    {
        var line = new CompileLine(2, TimeSpan.FromMinutes(1));
        var first = await line.EnterAsync();
        var second = await line.EnterAsync();

        Task<CompileLine.Slot> a, b;
        using (CompileSlots.ForRequest(CancellationToken.None, "u1:a")) a = line.EnterAsync();
        using (CompileSlots.ForRequest(CancellationToken.None, "u2:b")) b = line.EnterAsync();

        line.PlaceOf("u1:a").Should().Be(1, "it came first");
        line.PlaceOf("u2:b").Should().Be(2);
        line.WaitingCount.Should().Be(2);
        line.PlaceOf("u3:never").Should().BeNull();

        first.Dispose();
        using var slotA = await a.WaitAsync(TimeSpan.FromSeconds(5));
        line.PlaceOf("u1:a").Should().Be(0, "it is compiling");
        line.PlaceOf("u2:b").Should().Be(1, "it moved up");

        second.Dispose();
        using var slotB = await b.WaitAsync(TimeSpan.FromSeconds(5));
        slotA.Dispose();
        line.PlaceOf("u1:a").Should().BeNull("it finished");
        line.RunningCount.Should().Be(1);
    }

    [Fact]
    public async Task A_request_the_browser_gave_up_on_leaves_the_line()
    {
        var line = new CompileLine(1, TimeSpan.FromMinutes(1));
        using var held = await line.EnterAsync();
        using var browser = new CancellationTokenSource();

        Task<CompileLine.Slot> waiting;
        using (CompileSlots.ForRequest(browser.Token, "u1:gone")) waiting = line.EnterAsync();
        line.PlaceOf("u1:gone").Should().Be(1);

        browser.Cancel();
        var act = () => waiting;
        await act.Should().ThrowAsync<OperationCanceledException>();
        line.WaitingCount.Should().Be(0);
        line.PlaceOf("u1:gone").Should().BeNull();
    }

    [Fact]
    public async Task A_request_that_waits_too_long_is_told_to_try_again_rather_than_left_to_time_out()
    {
        var line = new CompileLine(1, TimeSpan.FromMilliseconds(100));
        using var held = await line.EnterAsync();

        Task<CompileLine.Slot> waiting;
        using (CompileSlots.ForRequest(CancellationToken.None, "u1:late")) waiting = line.EnterAsync();
        var act = () => waiting;
        (await act.Should().ThrowAsync<CompileBusyException>()).Which.Running.Should().Be(1);
        line.WaitingCount.Should().Be(0);
    }

    [Fact]
    public async Task Work_outside_a_request_waits_as_long_as_it_takes()
    {
        var line = new CompileLine(1, TimeSpan.FromMilliseconds(50));
        var held = await line.EnterAsync();
        var waiting = line.EnterAsync();
        await Task.Delay(300);
        waiting.IsCompleted.Should().BeFalse("only a request is given a limit");

        held.Dispose();
        using var slot = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        line.RunningCount.Should().Be(1);
    }

    [Fact]
    public async Task A_slot_given_back_twice_is_given_back_once()
    {
        var line = new CompileLine(1, TimeSpan.FromMinutes(1));
        var slot = await line.EnterAsync();
        slot.Dispose();
        slot.Dispose();
        line.RunningCount.Should().Be(0);
        using var again = await line.EnterAsync();
        var third = line.EnterAsync();
        third.IsCompleted.Should().BeFalse("one slot, not two");
        again.Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Background_work_detached_from_a_request_does_not_stop_with_it()
    {
        using var browser = new CancellationTokenSource();
        CancellationToken seen;
        using (CompileSlots.ForRequest(browser.Token, "u1:fmt"))
        {
            CompileSlots.Abandoned.CanBeCanceled.Should().BeTrue();
            seen = await Task.Run(() => { CompileSlots.Detach(); return CompileSlots.Abandoned; });
            CompileSlots.Abandoned.CanBeCanceled.Should().BeTrue("detaching inside the task leaves the request alone");
        }
        seen.CanBeCanceled.Should().BeFalse();
        CompileSlots.Abandoned.CanBeCanceled.Should().BeFalse("the request ended");
    }

    [Fact]
    public async Task A_compile_joined_from_another_request_that_went_away_is_compiled_again_for_this_one()
    {
        var cache = new CompileCache();
        var key = "joined-" + Guid.NewGuid().ToString("N");
        using var browser = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var compiles = 0;

        Task<(CompileCache.Entry Entry, bool Hit)> gone;
        using (CompileSlots.ForRequest(browser.Token, "u1:first"))
            gone = cache.GetOrCompileAsync(key, async () =>
            {
                Interlocked.Increment(ref compiles);
                started.SetResult();
                await Task.Delay(Timeout.Infinite, CompileSlots.Abandoned);
                return new CompileCache.Entry([1], "never", "");
            });
        await started.Task;

        var stays = cache.GetOrCompileAsync(key, async () =>
        {
            Interlocked.Increment(ref compiles);
            await Task.Yield();
            return new CompileCache.Entry([2], "mine", "");
        });
        browser.Cancel();

        var act = () => gone;
        await act.Should().ThrowAsync<OperationCanceledException>();
        var (entry, _) = await stays.WaitAsync(TimeSpan.FromSeconds(5));
        entry.Log.Should().Be("mine");
        compiles.Should().Be(2);
    }

    [Theory]
    [InlineData("user-1", "abc", "user-1:abc")]
    [InlineData("user-1", "", null)]
    [InlineData(null, "abc", null)]
    public void A_ticket_belongs_to_the_account_that_made_it(string? user, string ticket, string? kept) =>
        CompileController.TicketOf(user, ticket).Should().Be(kept);

    [Fact]
    public void A_ticket_longer_than_64_characters_is_refused() =>
        CompileController.TicketOf("user-1", new string('x', 65)).Should().BeNull();
}
