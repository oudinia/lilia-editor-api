using System.Data.Common;
using Lilia.Core.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lilia.Api.Services;

/// <summary>
/// Every database command's time, added to its request (<see cref="Timing"/>): the request log line
/// says how many queries a request made and how long they took. A command over
/// <see cref="SlowMs"/> is logged on its own with its SQL, never its parameter values (they can
/// hold an author's text; see the admin privacy model). The user, 18 Oct: "Add a query interceptor".
/// </summary>
public sealed class QueryTimingInterceptor : DbCommandInterceptor
{
    public const int SlowMs = 250;
    private readonly ILogger<QueryTimingInterceptor> _logger;

    public QueryTimingInterceptor(ILogger<QueryTimingInterceptor> logger) => _logger = logger;

    private void Record(DbCommand command, CommandExecutedEventData data)
    {
        Timing.Current?.AddDb(data.Duration);
        if (data.Duration.TotalMilliseconds >= SlowMs)
        {
            var sql = command.CommandText.ReplaceLineEndings(" ");
            _logger.LogWarning("[DB] slow query {Ms:0}ms: {Sql}", data.Duration.TotalMilliseconds, sql.Length > 400 ? sql[..400] + "…" : sql);
        }
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { Record(command, eventData); return result; }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    { Record(command, eventData); return ValueTask.FromResult(result); }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    { Record(command, eventData); return result; }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    { Record(command, eventData); return ValueTask.FromResult(result); }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    { Record(command, eventData); return result; }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    { Record(command, eventData); return ValueTask.FromResult(result); }
}
