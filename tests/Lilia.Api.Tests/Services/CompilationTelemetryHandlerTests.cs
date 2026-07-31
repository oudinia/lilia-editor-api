using FluentAssertions;
using Lilia.Api.Events.Common;
using Lilia.Api.Features.Telemetry.Handlers;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// P1.3 — compilation telemetry moves off <c>_ = Task.Run(…)</c> onto Wolverine.
///
/// <para>Two things are worth protecting. The mapping, because a field silently
/// dropped in telemetry nobody reads synchronously would go unnoticed
/// indefinitely. And the swallow, because this hangs off a compile a user is
/// waiting on — see
/// <see cref="A_failing_write_is_swallowed_rather_than_thrown"/>.</para>
///
/// <para><b>Why the mapping is tested without a database.</b> Not a preference:
/// the EF in-memory provider cannot map <c>LiliaDbContext</c> at all, because
/// <c>AiChat.Messages</c> is a <c>JsonDocument</c> and model validation throws
/// on first DbSet access. Persisting for real needs Testcontainers, i.e. Docker.
/// (Other in-memory tests in this suite pass only because they construct a
/// context and never persist through it — worth knowing before writing the
/// next one.)</para>
/// </summary>
public class CompilationTelemetryHandlerTests
{
    private static CompilationRecordedEvent AnEvent(
        string eventType = "validate_block",
        bool success = true) =>
        new(
            EventType: eventType,
            Success: success,
            WarningCount: 2,
            DurationMs: 137,
            DocumentId: Guid.NewGuid(),
            BlockId: Guid.NewGuid(),
            BlockType: "equation",
            ErrorRaw: success ? null : "! Undefined control sequence.",
            ErrorCategory: success ? null : "undefined_command",
            ErrorToken: success ? null : @"\foo",
            ErrorLine: success ? null : 42,
            UserId: "user_123");

    [Fact]
    public void Every_field_on_the_event_reaches_the_row()
    {
        var evt = AnEvent(success: false);

        var row = PersistCompilationEventHandler.ToEntity(evt);

        row.EventType.Should().Be(evt.EventType);
        row.Success.Should().BeFalse();
        row.WarningCount.Should().Be(2);
        row.DurationMs.Should().Be(137);
        row.DocumentId.Should().Be(evt.DocumentId);
        row.BlockId.Should().Be(evt.BlockId);
        row.BlockType.Should().Be("equation");
        row.ErrorRaw.Should().Be(evt.ErrorRaw);
        row.ErrorCategory.Should().Be(evt.ErrorCategory);
        row.ErrorToken.Should().Be(evt.ErrorToken);
        row.ErrorLine.Should().Be(42);
        row.UserId.Should().Be("user_123");
    }

    [Fact]
    public void A_successful_compile_records_no_error_detail()
    {
        var row = PersistCompilationEventHandler.ToEntity(AnEvent());

        row.Success.Should().BeTrue();
        row.ErrorRaw.Should().BeNull();
        row.ErrorCategory.Should().BeNull();
        row.ErrorToken.Should().BeNull();
        row.ErrorLine.Should().BeNull();
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("validate_block")]
    [InlineData("validate_document")]
    public void Each_compile_path_is_recorded_under_its_own_event_type(string eventType)
    {
        PersistCompilationEventHandler.ToEntity(AnEvent(eventType))
            .EventType.Should().Be(eventType);
    }

    [Fact]
    public void An_anonymous_compile_records_a_null_user()
    {
        var evt = AnEvent() with { UserId = null, DocumentId = null, BlockId = null, BlockType = null };

        var row = PersistCompilationEventHandler.ToEntity(evt);

        row.UserId.Should().BeNull();
        row.DocumentId.Should().BeNull();
        row.BlockId.Should().BeNull();
        row.BlockType.Should().BeNull();
    }

    [Fact]
    public void Each_row_gets_its_own_identity()
    {
        // Duplicate delivery is explicitly allowed by the event contract, so two
        // mappings of the same event must not collide on the primary key.
        var evt = AnEvent();

        PersistCompilationEventHandler.ToEntity(evt).Id
            .Should().NotBe(PersistCompilationEventHandler.ToEntity(evt).Id);
    }

    [Fact]
    public async Task A_failing_write_is_swallowed_rather_than_thrown()
    {
        // The property that matters. Telemetry hangs off a compile the author is
        // waiting on: if recording the outcome fails, they already have their
        // answer and nothing about their document is wrong. A throw here would
        // surface as a failed message and, with retries, a poisoned queue — over
        // a row nobody reads synchronously.
        //
        // The in-memory provider cannot map this context (see the class remarks),
        // so simply touching a DbSet throws — which makes it a perfectly good
        // failing database for this particular assertion.
        await using var db = new LiliaDbContext(
            new DbContextOptionsBuilder<LiliaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var act = async () => await new PersistCompilationEventHandler().Handle(
            AnEvent(), db, NullLogger<PersistCompilationEventHandler>.Instance, default);

        await act.Should().NotThrowAsync();
    }
}
