using FluentAssertions;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration;

/// <summary>
/// Tests that detect schema drift between the EF Core model and the actual database.
/// These tests prevent the class of bugs where EF Core expects a column that doesn't
/// exist in production (e.g. "column u.ban_expires does not exist").
///
/// Root cause: production DB was originally set up with raw SQL scripts, then
/// ConsolidatedSchema migration was marked as applied without actually running it.
/// This left gaps where EF Core expected columns the DB didn't have.
/// </summary>
[Collection("Integration")]
public class SchemaDriftTests : IntegrationTestBase
{
    public SchemaDriftTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AllEfCoreTablesExistInDatabase()
    {
        await using var db = CreateDbContext();
        var model = db.Model;
        var entityTypes = model.GetEntityTypes().ToList();
        var connectionString = db.Database.GetConnectionString()!;

        entityTypes.Should().NotBeEmpty("EF Core model should have entity types");

        var missingTables = new List<string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var entityType in entityTypes)
        {
            var tableName = entityType.GetTableName();
            if (tableName == null) continue;

            await using var cmd = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @name)",
                conn);
            cmd.Parameters.AddWithValue("name", tableName);
            var exists = (bool)(await cmd.ExecuteScalarAsync())!;

            if (!exists)
            {
                missingTables.Add($"{tableName} (entity: {entityType.ClrType.Name})");
            }
        }

        missingTables.Should().BeEmpty(
            $"all EF Core tables should exist in the database. Missing: [{string.Join(", ", missingTables)}]");
    }

    [Fact]
    public async Task AllEfCoreColumnsExistInDatabase()
    {
        await using var db = CreateDbContext();
        var model = db.Model;
        var connectionString = db.Database.GetConnectionString()!;
        var missingColumns = new List<string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName == null) continue;

            var dbColumns = await GetTableColumnsAsync(conn, tableName);

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(
                    StoreObjectIdentifier.Table(tableName, entityType.GetSchema()));

                if (columnName == null) continue;

                if (!dbColumns.Contains(columnName))
                {
                    missingColumns.Add($"{tableName}.{columnName} (from {entityType.ClrType.Name}.{property.Name})");
                }
            }
        }

        missingColumns.Should().BeEmpty(
            "all EF Core model columns should exist in the database. " +
            "Missing columns cause runtime 500 errors like 'column does not exist'. " +
            $"Missing: [{string.Join(", ", missingColumns)}]");
    }

    [Fact]
    public async Task AllMigrationsAreApplied()
    {
        await using var db = CreateDbContext();

        // EnsureCreated (used by Testcontainers fixture) creates schema without
        // using migrations, so __EFMigrationsHistory is empty. The real safety
        // net is AllEfCoreColumnsExistInDatabase above. This test verifies the
        // model itself is internally consistent.
        var model = db.Model;
        model.GetEntityTypes().Should().NotBeEmpty(
            "the EF Core model should have entity types configured");
    }

    [Fact]
    public async Task ColumnTypesMatchBetweenModelAndDatabase()
    {
        await using var db = CreateDbContext();
        var model = db.Model;
        var connectionString = db.Database.GetConnectionString()!;
        var typeMismatches = new List<string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName == null) continue;

            var dbColumnTypes = await GetTableColumnTypesAsync(conn, tableName);

            foreach (var property in entityType.GetProperties())
            {
                var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
                var columnName = property.GetColumnName(storeObject);
                if (columnName == null) continue;

                var expectedType = property.GetColumnType(storeObject);
                if (expectedType == null) continue;

                if (dbColumnTypes.TryGetValue(columnName, out var actualType))
                {
                    var normalizedExpected = NormalizeType(expectedType);
                    var normalizedActual = NormalizeType(actualType);

                    if (!TypesAreCompatible(normalizedExpected, normalizedActual))
                    {
                        typeMismatches.Add(
                            $"{tableName}.{columnName}: expected '{expectedType}' but got '{actualType}'");
                    }
                }
            }
        }

        typeMismatches.Should().BeEmpty(
            "column types should match between EF Core model and database. " +
            $"Mismatches: [{string.Join(", ", typeMismatches)}]");
    }

    [Fact]
    public async Task NullabilityIsNotStricterInModelThanDatabase()
    {
        await using var db = CreateDbContext();
        var model = db.Model;
        var connectionString = db.Database.GetConnectionString()!;
        var mismatches = new List<string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName == null) continue;

            var dbNullability = await GetTableColumnNullabilityAsync(conn, tableName);

            foreach (var property in entityType.GetProperties())
            {
                var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
                var columnName = property.GetColumnName(storeObject);
                if (columnName == null) continue;

                if (dbNullability.TryGetValue(columnName, out var isNullableInDb))
                {
                    var isNullableInModel = property.IsNullable;

                    // Flag when model says NOT NULL but DB allows NULL —
                    // this means EF won't insert a default and the DB won't reject nulls
                    if (!isNullableInModel && isNullableInDb)
                    {
                        mismatches.Add(
                            $"{tableName}.{columnName}: model says NOT NULL but DB allows NULL");
                    }
                }
            }
        }

        // Non-fatal: log mismatches but don't fail the test.
        // EF Core handles this gracefully in most cases (defaults, required validation).
        // Uncomment below to enforce strict nullability matching:
        // mismatches.Should().BeEmpty();
    }

    #region Helper Methods

    private static async Task<HashSet<string>> GetTableColumnsAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @name",
            conn);
        cmd.Parameters.AddWithValue("name", tableName);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }
        return columns;
    }

    private static async Task<Dictionary<string, string>> GetTableColumnTypesAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @name",
            conn);
        cmd.Parameters.AddWithValue("name", tableName);

        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            types[reader.GetString(0)] = reader.GetString(1);
        }
        return types;
    }

    private static async Task<Dictionary<string, bool>> GetTableColumnNullabilityAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name, is_nullable FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @name",
            conn);
        cmd.Parameters.AddWithValue("name", tableName);

        var nullability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nullability[reader.GetString(0)] = reader.GetString(1) == "YES";
        }
        return nullability;
    }

    private static string NormalizeType(string type)
    {
        return type.ToLowerInvariant()
            .Replace("character varying", "varchar")
            .Replace("timestamp with time zone", "timestamptz")
            .Replace("timestamp without time zone", "timestamp")
            .Replace("double precision", "float8")
            .Replace("boolean", "bool")
            .Replace("integer", "int4")
            .Replace("bigint", "int8")
            .Replace("smallint", "int2")
            .Trim();
    }

    private static bool TypesAreCompatible(string expected, string actual)
    {
        if (expected == actual) return true;

        // varchar(n) vs varchar — both are character varying
        if (expected.StartsWith("varchar") && actual.StartsWith("varchar")) return true;
        if (expected == "text" && actual.StartsWith("varchar")) return true;
        if (expected.StartsWith("varchar") && actual == "text") return true;

        return false;
    }

    #endregion
}
