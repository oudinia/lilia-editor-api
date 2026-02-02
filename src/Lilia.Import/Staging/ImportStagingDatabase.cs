using Microsoft.Data.Sqlite;
using Lilia.Core.Models;
using Lilia.Import.Models;

namespace Lilia.Import.Staging;

/// <summary>
/// Database-driven import staging service that persists import operations for crash recovery,
/// user review, and governance over import/export operations.
/// </summary>
public class ImportStagingDatabase : IDisposable
{
    private readonly string _databasePath;
    private SqliteConnection? _connection;
    private bool _disposed;

    private const string StagingSchema = """
        -- Import sessions (one per import operation)
        CREATE TABLE IF NOT EXISTS import_session (
            id INTEGER PRIMARY KEY,
            source_file TEXT NOT NULL,
            source_format TEXT NOT NULL,
            document_title TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'pending',
            created_at TEXT NOT NULL,
            modified_at TEXT NOT NULL,
            accepted_at TEXT,
            error_message TEXT
        );

        -- Staged sections (mirrors Lilia section table)
        CREATE TABLE IF NOT EXISTS staged_section (
            id INTEGER PRIMARY KEY,
            session_id INTEGER NOT NULL REFERENCES import_session(id) ON DELETE CASCADE,
            parent_id INTEGER REFERENCES staged_section(id),
            title TEXT NOT NULL,
            sort_order INTEGER NOT NULL,
            created_at TEXT NOT NULL
        );

        -- Staged blocks (mirrors Lilia block table)
        CREATE TABLE IF NOT EXISTS staged_block (
            id INTEGER PRIMARY KEY,
            session_id INTEGER NOT NULL REFERENCES import_session(id) ON DELETE CASCADE,
            section_id INTEGER NOT NULL REFERENCES staged_section(id) ON DELETE CASCADE,
            block_type TEXT NOT NULL,
            content TEXT NOT NULL,
            sort_order INTEGER NOT NULL,
            original_block_type TEXT,
            is_deleted INTEGER DEFAULT 0,
            is_modified INTEGER DEFAULT 0,
            created_at TEXT NOT NULL,
            modified_at TEXT NOT NULL
        );

        -- Staged assets (images, files)
        CREATE TABLE IF NOT EXISTS staged_asset (
            id INTEGER PRIMARY KEY,
            session_id INTEGER NOT NULL REFERENCES import_session(id) ON DELETE CASCADE,
            filename TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            data BLOB NOT NULL,
            hash TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        -- Block-to-asset references
        CREATE TABLE IF NOT EXISTS staged_block_asset (
            block_id INTEGER REFERENCES staged_block(id) ON DELETE CASCADE,
            asset_id INTEGER REFERENCES staged_asset(id) ON DELETE CASCADE,
            PRIMARY KEY (block_id, asset_id)
        );

        -- Import issues/warnings detected
        CREATE TABLE IF NOT EXISTS import_issue (
            id INTEGER PRIMARY KEY,
            session_id INTEGER NOT NULL REFERENCES import_session(id) ON DELETE CASCADE,
            severity TEXT NOT NULL,
            category TEXT NOT NULL,
            description TEXT NOT NULL,
            details TEXT,
            affected_section_id INTEGER REFERENCES staged_section(id),
            affected_block_id INTEGER REFERENCES staged_block(id),
            is_resolved INTEGER DEFAULT 0,
            resolved_at TEXT,
            resolution_note TEXT
        );

        -- Import statistics
        CREATE TABLE IF NOT EXISTS import_statistics (
            id INTEGER PRIMARY KEY,
            session_id INTEGER NOT NULL REFERENCES import_session(id) ON DELETE CASCADE,
            total_elements_parsed INTEGER DEFAULT 0,
            sections_created INTEGER DEFAULT 0,
            blocks_created INTEGER DEFAULT 0,
            equations_found INTEGER DEFAULT 0,
            equations_converted INTEGER DEFAULT 0,
            images_extracted INTEGER DEFAULT 0,
            tables_extracted INTEGER DEFAULT 0,
            code_blocks_detected INTEGER DEFAULT 0,
            parse_time_ms INTEGER DEFAULT 0,
            convert_time_ms INTEGER DEFAULT 0
        );

        -- User corrections history (for audit/governance)
        CREATE TABLE IF NOT EXISTS import_correction (
            id INTEGER PRIMARY KEY,
            session_id INTEGER NOT NULL REFERENCES import_session(id) ON DELETE CASCADE,
            correction_type TEXT NOT NULL,
            target_type TEXT NOT NULL,
            target_id INTEGER NOT NULL,
            old_value TEXT,
            new_value TEXT,
            corrected_at TEXT NOT NULL
        );

        -- Indexes
        CREATE INDEX IF NOT EXISTS idx_staged_section_session ON staged_section(session_id);
        CREATE INDEX IF NOT EXISTS idx_staged_block_session ON staged_block(session_id);
        CREATE INDEX IF NOT EXISTS idx_staged_block_section ON staged_block(section_id);
        CREATE INDEX IF NOT EXISTS idx_import_issue_session ON import_issue(session_id);
        CREATE INDEX IF NOT EXISTS idx_import_session_status ON import_session(status);
        """;

    /// <summary>
    /// Creates a new import staging database.
    /// </summary>
    /// <param name="databasePath">Path to the staging database file. If null, uses default location.</param>
    public ImportStagingDatabase(string? databasePath = null)
    {
        _databasePath = databasePath ?? GetDefaultDatabasePath();
        EnsureDirectoryExists();
        InitializeDatabase();
    }

    private static string GetDefaultDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Lilia", "staging", "import_staging.db");
    }

    private void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void InitializeDatabase()
    {
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();

        // Configure pragmas
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;
                PRAGMA cache_size=-16000;
                """;
            cmd.ExecuteNonQuery();
        }

        // Create schema
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = StagingSchema;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Creates a new import session and returns its ID.
    /// </summary>
    public int CreateSession(string sourceFile, string sourceFormat, string documentTitle)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO import_session (source_file, source_format, document_title, status, created_at, modified_at)
            VALUES (@source_file, @source_format, @document_title, 'pending', @created_at, @modified_at);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@source_file", sourceFile);
        cmd.Parameters.AddWithValue("@source_format", sourceFormat);
        cmd.Parameters.AddWithValue("@document_title", documentTitle);
        cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Stages an import result into the database.
    /// </summary>
    public int StageImportResult(ImportResult result, string sourceFile, string sourceFormat)
    {
        if (result.Document == null)
            throw new ArgumentException("ImportResult must have a Document");

        var sessionId = CreateSession(sourceFile, sourceFormat, result.Document.Title);

        // Stage statistics
        StageStatistics(sessionId, result.Statistics);

        // Stage sections and blocks with ID mapping
        var sectionIdMap = new Dictionary<int, int>(); // original ID -> staged ID

        // First pass: insert all sections to get their staged IDs
        foreach (var section in result.Sections)
        {
            var stagedSectionId = StageSectionWithoutParent(sessionId, section);
            sectionIdMap[section.Id] = stagedSectionId;
        }

        // Second pass: update parent IDs
        foreach (var section in result.Sections.Where(s => s.ParentId.HasValue))
        {
            UpdateStagedSectionParent(sectionIdMap[section.Id], sectionIdMap[section.ParentId!.Value]);
        }

        // Stage blocks
        foreach (var section in result.Sections)
        {
            foreach (var block in section.Blocks)
            {
                StageBlock(sessionId, sectionIdMap[section.Id], block);
            }
        }

        // Stage warnings as issues
        foreach (var warning in result.Warnings)
        {
            StageWarning(sessionId, warning);
        }

        // Detect and stage additional issues
        DetectAndStageIssues(sessionId, result);

        return sessionId;
    }

    private void StageStatistics(int sessionId, ImportStatistics stats)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO import_statistics
            (session_id, total_elements_parsed, sections_created, blocks_created,
             equations_found, equations_converted, images_extracted, tables_extracted,
             code_blocks_detected, parse_time_ms, convert_time_ms)
            VALUES
            (@session_id, @total_elements_parsed, @sections_created, @blocks_created,
             @equations_found, @equations_converted, @images_extracted, @tables_extracted,
             @code_blocks_detected, @parse_time_ms, @convert_time_ms);
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@total_elements_parsed", stats.TotalElementsParsed);
        cmd.Parameters.AddWithValue("@sections_created", stats.SectionsCreated);
        cmd.Parameters.AddWithValue("@blocks_created", stats.BlocksCreated);
        cmd.Parameters.AddWithValue("@equations_found", stats.EquationsFound);
        cmd.Parameters.AddWithValue("@equations_converted", stats.EquationsConverted);
        cmd.Parameters.AddWithValue("@images_extracted", stats.ImagesExtracted);
        cmd.Parameters.AddWithValue("@tables_extracted", stats.TablesExtracted);
        cmd.Parameters.AddWithValue("@code_blocks_detected", stats.CodeBlocksDetected);
        cmd.Parameters.AddWithValue("@parse_time_ms", (int)stats.ParseTime.TotalMilliseconds);
        cmd.Parameters.AddWithValue("@convert_time_ms", (int)stats.ConvertTime.TotalMilliseconds);
        cmd.ExecuteNonQuery();
    }

    private int StageSectionWithoutParent(int sessionId, Section section)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO staged_section (session_id, title, sort_order, created_at)
            VALUES (@session_id, @title, @sort_order, @created_at);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@title", section.Title);
        cmd.Parameters.AddWithValue("@sort_order", section.SortOrder);
        cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void UpdateStagedSectionParent(int stagedSectionId, int stagedParentId)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "UPDATE staged_section SET parent_id = @parent_id WHERE id = @id";
        cmd.Parameters.AddWithValue("@parent_id", stagedParentId);
        cmd.Parameters.AddWithValue("@id", stagedSectionId);
        cmd.ExecuteNonQuery();
    }

    private int StageBlock(int sessionId, int stagedSectionId, Block block)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO staged_block
            (session_id, section_id, block_type, content, sort_order, original_block_type, created_at, modified_at)
            VALUES
            (@session_id, @section_id, @block_type, @content, @sort_order, @original_block_type, @created_at, @modified_at);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@section_id", stagedSectionId);
        cmd.Parameters.AddWithValue("@block_type", block.BlockType.ToString());
        cmd.Parameters.AddWithValue("@content", block.Content);
        cmd.Parameters.AddWithValue("@sort_order", block.SortOrder);
        cmd.Parameters.AddWithValue("@original_block_type", block.BlockType.ToString());
        cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));

        var stagedBlockId = Convert.ToInt32(cmd.ExecuteScalar());

        // Stage any assets attached to this block
        foreach (var asset in block.Assets)
        {
            var stagedAssetId = StageAsset(sessionId, asset);
            LinkBlockToAsset(stagedBlockId, stagedAssetId);
        }

        return stagedBlockId;
    }

    private int StageAsset(int sessionId, Asset asset)
    {
        // Check if asset with same hash already exists in this session
        using (var checkCmd = _connection!.CreateCommand())
        {
            checkCmd.CommandText = "SELECT id FROM staged_asset WHERE session_id = @session_id AND hash = @hash";
            checkCmd.Parameters.AddWithValue("@session_id", sessionId);
            checkCmd.Parameters.AddWithValue("@hash", asset.Hash);
            var existingId = checkCmd.ExecuteScalar();
            if (existingId != null)
            {
                return Convert.ToInt32(existingId);
            }
        }

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO staged_asset (session_id, filename, mime_type, data, hash, created_at)
            VALUES (@session_id, @filename, @mime_type, @data, @hash, @created_at);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@filename", asset.Filename);
        cmd.Parameters.AddWithValue("@mime_type", asset.MimeType);
        cmd.Parameters.AddWithValue("@data", asset.Data);
        cmd.Parameters.AddWithValue("@hash", asset.Hash);
        cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void LinkBlockToAsset(int stagedBlockId, int stagedAssetId)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO staged_block_asset (block_id, asset_id)
            VALUES (@block_id, @asset_id);
            """;
        cmd.Parameters.AddWithValue("@block_id", stagedBlockId);
        cmd.Parameters.AddWithValue("@asset_id", stagedAssetId);
        cmd.ExecuteNonQuery();
    }

    private void StageWarning(int sessionId, ImportWarning warning)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO import_issue (session_id, severity, category, description, details)
            VALUES (@session_id, 'Warning', @category, @description, @details);
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@category", warning.Type.ToString());
        cmd.Parameters.AddWithValue("@description", warning.Message);
        cmd.Parameters.AddWithValue("@details", warning.Details ?? "");
        cmd.ExecuteNonQuery();
    }

    private void DetectAndStageIssues(int sessionId, ImportResult result)
    {
        // Check document title
        if (string.IsNullOrWhiteSpace(result.Document?.Title) || result.Document.Title == "Imported Document")
        {
            StageIssue(sessionId, "Info", "Document Title",
                "Document title may not be correctly detected",
                "Please review and edit the document title if needed.");
        }

        // Check for excessive code blocks
        var totalBlocks = result.Sections.Sum(s => s.Blocks.Count);
        var codeBlocks = result.Sections.Sum(s => s.Blocks.Count(b => b.BlockType == BlockType.Code));
        var codeRatio = totalBlocks > 0 ? (double)codeBlocks / totalBlocks : 0;

        if (codeRatio > 0.3 && codeBlocks > 10)
        {
            StageIssue(sessionId, "Warning", "Code Detection",
                $"{codeBlocks} code blocks detected ({codeRatio:P0} of total)",
                "Some paragraphs may have been incorrectly classified as code blocks due to monospace fonts or styling.");
        }

        // Check for empty sections
        var emptySections = result.Sections.Where(s => s.Blocks.Count == 0 &&
            !result.Sections.Any(c => c.ParentId == s.Id)).ToList();
        if (emptySections.Count > 0)
        {
            StageIssue(sessionId, "Info", "Empty Sections",
                $"{emptySections.Count} empty sections found",
                $"Sections with no content: {string.Join(", ", emptySections.Take(5).Select(s => s.Title))}");
        }

        // Check for very short section titles
        var shortTitles = result.Sections.Where(s => s.Title.Length < 3).ToList();
        if (shortTitles.Count > 0)
        {
            StageIssue(sessionId, "Warning", "Section Titles",
                $"{shortTitles.Count} sections have very short titles",
                "These might be incorrectly detected headings.");
        }

        // Check for equation conversion issues
        if (result.Statistics.EquationsFound > 0 &&
            result.Statistics.EquationsConverted < result.Statistics.EquationsFound)
        {
            StageIssue(sessionId, "Warning", "Equation Conversion",
                $"{result.Statistics.EquationsFound} equations found but {result.Statistics.EquationsConverted} converted",
                "Some equations may not have been successfully converted to LaTeX format.");
        }
    }

    private void StageIssue(int sessionId, string severity, string category, string description, string details)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO import_issue (session_id, severity, category, description, details)
            VALUES (@session_id, @severity, @category, @description, @details);
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@severity", severity);
        cmd.Parameters.AddWithValue("@category", category);
        cmd.Parameters.AddWithValue("@description", description);
        cmd.Parameters.AddWithValue("@details", details);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets session info by ID.
    /// </summary>
    public StagedSession? GetSession(int sessionId)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_file, source_format, document_title, status, created_at, modified_at
            FROM import_session WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", sessionId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new StagedSession
            {
                Id = reader.GetInt32(0),
                SourceFile = reader.GetString(1),
                SourceFormat = reader.GetString(2),
                DocumentTitle = reader.GetString(3),
                Status = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                ModifiedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        return null;
    }

    /// <summary>
    /// Gets all pending sessions.
    /// </summary>
    public List<StagedSession> GetPendingSessions()
    {
        var sessions = new List<StagedSession>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_file, source_format, document_title, status, created_at, modified_at
            FROM import_session WHERE status = 'pending' ORDER BY created_at DESC;
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new StagedSession
            {
                Id = reader.GetInt32(0),
                SourceFile = reader.GetString(1),
                SourceFormat = reader.GetString(2),
                DocumentTitle = reader.GetString(3),
                Status = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                ModifiedAt = DateTime.Parse(reader.GetString(6))
            });
        }
        return sessions;
    }

    /// <summary>
    /// Gets all sections for a session.
    /// </summary>
    public List<StagedSection> GetSections(int sessionId)
    {
        var sections = new List<StagedSection>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, parent_id, title, sort_order
            FROM staged_section WHERE session_id = @session_id ORDER BY sort_order;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sections.Add(new StagedSection
            {
                Id = reader.GetInt32(0),
                ParentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Title = reader.GetString(2),
                SortOrder = reader.GetInt32(3)
            });
        }
        return sections;
    }

    /// <summary>
    /// Gets all blocks for a session.
    /// </summary>
    public List<StagedBlock> GetBlocks(int sessionId)
    {
        var blocks = new List<StagedBlock>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, section_id, block_type, content, sort_order, original_block_type, is_deleted, is_modified
            FROM staged_block WHERE session_id = @session_id AND is_deleted = 0 ORDER BY section_id, sort_order;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            blocks.Add(new StagedBlock
            {
                Id = reader.GetInt32(0),
                SectionId = reader.GetInt32(1),
                BlockType = Enum.Parse<BlockType>(reader.GetString(2)),
                Content = reader.GetString(3),
                SortOrder = reader.GetInt32(4),
                OriginalBlockType = Enum.Parse<BlockType>(reader.GetString(5)),
                IsDeleted = reader.GetInt32(6) == 1,
                IsModified = reader.GetInt32(7) == 1
            });
        }
        return blocks;
    }

    /// <summary>
    /// Gets all assets for a staged block.
    /// </summary>
    public List<Asset> GetAssetsForBlock(int stagedBlockId)
    {
        var assets = new List<Asset>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT a.id, a.filename, a.mime_type, a.data, a.hash, a.created_at
            FROM staged_asset a
            INNER JOIN staged_block_asset ba ON a.id = ba.asset_id
            WHERE ba.block_id = @block_id;
            """;
        cmd.Parameters.AddWithValue("@block_id", stagedBlockId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            assets.Add(new Asset
            {
                Id = reader.GetInt32(0),
                Filename = reader.GetString(1),
                MimeType = reader.GetString(2),
                Data = (byte[])reader.GetValue(3),
                Hash = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5))
            });
        }
        return assets;
    }

    /// <summary>
    /// Gets all assets for a session.
    /// </summary>
    public List<StagedAsset> GetAssets(int sessionId)
    {
        var assets = new List<StagedAsset>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, filename, mime_type, hash, created_at
            FROM staged_asset WHERE session_id = @session_id ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            assets.Add(new StagedAsset
            {
                Id = reader.GetInt32(0),
                Filename = reader.GetString(1),
                MimeType = reader.GetString(2),
                Hash = reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4))
            });
        }
        return assets;
    }

    /// <summary>
    /// Gets all issues for a session.
    /// </summary>
    public List<StagedIssue> GetIssues(int sessionId)
    {
        var issues = new List<StagedIssue>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT id, severity, category, description, details, affected_section_id, affected_block_id, is_resolved
            FROM import_issue WHERE session_id = @session_id ORDER BY
                CASE severity WHEN 'Error' THEN 1 WHEN 'Warning' THEN 2 ELSE 3 END;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            issues.Add(new StagedIssue
            {
                Id = reader.GetInt32(0),
                Severity = reader.GetString(1),
                Category = reader.GetString(2),
                Description = reader.GetString(3),
                Details = reader.IsDBNull(4) ? "" : reader.GetString(4),
                AffectedSectionId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                AffectedBlockId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                IsResolved = reader.GetInt32(7) == 1
            });
        }
        return issues;
    }

    /// <summary>
    /// Gets statistics for a session.
    /// </summary>
    public StagedStatistics? GetStatistics(int sessionId)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT total_elements_parsed, sections_created, blocks_created,
                   equations_found, equations_converted, images_extracted,
                   tables_extracted, code_blocks_detected, parse_time_ms, convert_time_ms
            FROM import_statistics WHERE session_id = @session_id;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new StagedStatistics
            {
                TotalElementsParsed = reader.GetInt32(0),
                SectionsCreated = reader.GetInt32(1),
                BlocksCreated = reader.GetInt32(2),
                EquationsFound = reader.GetInt32(3),
                EquationsConverted = reader.GetInt32(4),
                ImagesExtracted = reader.GetInt32(5),
                TablesExtracted = reader.GetInt32(6),
                CodeBlocksDetected = reader.GetInt32(7),
                ParseTimeMs = reader.GetInt32(8),
                ConvertTimeMs = reader.GetInt32(9)
            };
        }
        return null;
    }

    /// <summary>
    /// Updates the document title for a session.
    /// </summary>
    public void UpdateDocumentTitle(int sessionId, string newTitle)
    {
        RecordCorrection(sessionId, "TitleChange", "Session", sessionId, null, newTitle);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            UPDATE import_session SET document_title = @title, modified_at = @modified_at WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@title", newTitle);
        cmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Updates the block type for a staged block.
    /// </summary>
    public void UpdateBlockType(int blockId, BlockType newType)
    {
        // Get current block type for correction record
        string? oldType = null;
        int? sessionId = null;
        using (var getCmd = _connection!.CreateCommand())
        {
            getCmd.CommandText = "SELECT block_type, session_id FROM staged_block WHERE id = @id";
            getCmd.Parameters.AddWithValue("@id", blockId);
            using var reader = getCmd.ExecuteReader();
            if (reader.Read())
            {
                oldType = reader.GetString(0);
                sessionId = reader.GetInt32(1);
            }
        }

        if (sessionId.HasValue)
        {
            RecordCorrection(sessionId.Value, "BlockTypeChange", "Block", blockId, oldType, newType.ToString());
        }

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            UPDATE staged_block SET block_type = @block_type, is_modified = 1, modified_at = @modified_at WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@block_type", newType.ToString());
        cmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", blockId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Marks a block as deleted (soft delete).
    /// </summary>
    public void DeleteBlock(int blockId)
    {
        int? sessionId = null;
        using (var getCmd = _connection!.CreateCommand())
        {
            getCmd.CommandText = "SELECT session_id FROM staged_block WHERE id = @id";
            getCmd.Parameters.AddWithValue("@id", blockId);
            sessionId = Convert.ToInt32(getCmd.ExecuteScalar());
        }

        if (sessionId.HasValue)
        {
            RecordCorrection(sessionId.Value, "BlockDelete", "Block", blockId, null, null);
        }

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            UPDATE staged_block SET is_deleted = 1, modified_at = @modified_at WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", blockId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Merges a block with the previous block.
    /// </summary>
    public void MergeBlockWithPrevious(int blockId)
    {
        // Get current block info
        int? sectionId = null;
        int? sortOrder = null;
        string? content = null;
        int? sessionId = null;

        using (var getCmd = _connection!.CreateCommand())
        {
            getCmd.CommandText = "SELECT section_id, sort_order, content, session_id FROM staged_block WHERE id = @id";
            getCmd.Parameters.AddWithValue("@id", blockId);
            using var reader = getCmd.ExecuteReader();
            if (reader.Read())
            {
                sectionId = reader.GetInt32(0);
                sortOrder = reader.GetInt32(1);
                content = reader.GetString(2);
                sessionId = reader.GetInt32(3);
            }
        }

        if (!sectionId.HasValue || !sortOrder.HasValue || sortOrder <= 0) return;

        // Find previous block
        int? prevBlockId = null;
        using (var findCmd = _connection!.CreateCommand())
        {
            findCmd.CommandText = """
                SELECT id FROM staged_block
                WHERE section_id = @section_id AND sort_order < @sort_order AND is_deleted = 0
                ORDER BY sort_order DESC LIMIT 1;
                """;
            findCmd.Parameters.AddWithValue("@section_id", sectionId);
            findCmd.Parameters.AddWithValue("@sort_order", sortOrder);
            var result = findCmd.ExecuteScalar();
            if (result != null) prevBlockId = Convert.ToInt32(result);
        }

        if (!prevBlockId.HasValue) return;

        if (sessionId.HasValue)
        {
            RecordCorrection(sessionId.Value, "BlockMerge", "Block", blockId, null, $"Merged into block {prevBlockId}");
        }

        // Append content to previous block
        using (var updateCmd = _connection!.CreateCommand())
        {
            updateCmd.CommandText = """
                UPDATE staged_block SET content = content || @separator || @content, is_modified = 1, modified_at = @modified_at
                WHERE id = @id;
                """;
            updateCmd.Parameters.AddWithValue("@separator", "\n\n");
            updateCmd.Parameters.AddWithValue("@content", content);
            updateCmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));
            updateCmd.Parameters.AddWithValue("@id", prevBlockId);
            updateCmd.ExecuteNonQuery();
        }

        // Mark current block as deleted
        DeleteBlock(blockId);
    }

    private void RecordCorrection(int sessionId, string correctionType, string targetType, int targetId, string? oldValue, string? newValue)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO import_correction (session_id, correction_type, target_type, target_id, old_value, new_value, corrected_at)
            VALUES (@session_id, @correction_type, @target_type, @target_id, @old_value, @new_value, @corrected_at);
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@correction_type", correctionType);
        cmd.Parameters.AddWithValue("@target_type", targetType);
        cmd.Parameters.AddWithValue("@target_id", targetId);
        cmd.Parameters.AddWithValue("@old_value", oldValue ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@new_value", newValue ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@corrected_at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Marks an issue as resolved.
    /// </summary>
    public void ResolveIssue(int issueId, string? resolutionNote = null)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            UPDATE import_issue SET is_resolved = 1, resolved_at = @resolved_at, resolution_note = @note WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@resolved_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@note", resolutionNote ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", issueId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Marks a session as accepted.
    /// </summary>
    public void AcceptSession(int sessionId)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            UPDATE import_session SET status = 'accepted', accepted_at = @accepted_at, modified_at = @modified_at WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@accepted_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Marks a session as cancelled.
    /// </summary>
    public void CancelSession(int sessionId)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            UPDATE import_session SET status = 'cancelled', modified_at = @modified_at WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@modified_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets the accepted session data ready for copying to the final document.
    /// </summary>
    public ImportResult BuildAcceptedResult(int sessionId)
    {
        var session = GetSession(sessionId);
        if (session == null || session.Status != "accepted")
            throw new InvalidOperationException("Session not found or not accepted");

        var sections = GetSections(sessionId);
        var blocks = GetBlocks(sessionId);
        var stats = GetStatistics(sessionId);

        var document = new Document
        {
            Id = 1, // Will be assigned when saving
            Title = session.DocumentTitle,
            CreatedAt = session.CreatedAt,
            ModifiedAt = DateTime.UtcNow
        };

        // Build section list with blocks
        var sectionList = new List<Section>();
        var sectionIdMap = new Dictionary<int, int>(); // staged ID -> new ID
        var newSectionId = 1;

        foreach (var stagedSection in sections)
        {
            var section = new Section
            {
                Id = newSectionId,
                Title = stagedSection.Title,
                SortOrder = stagedSection.SortOrder,
                CreatedAt = DateTime.UtcNow
            };

            sectionIdMap[stagedSection.Id] = newSectionId;
            newSectionId++;

            // Add blocks for this section
            var sectionBlocks = blocks.Where(b => b.SectionId == stagedSection.Id).OrderBy(b => b.SortOrder);
            var blockSortOrder = 0;
            foreach (var stagedBlock in sectionBlocks)
            {
                var block = new Block
                {
                    Id = 0, // Will be assigned when saving
                    SectionId = section.Id,
                    BlockType = stagedBlock.BlockType,
                    Content = stagedBlock.Content,
                    SortOrder = blockSortOrder++,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                };

                // Include any assets attached to this block
                var blockAssets = GetAssetsForBlock(stagedBlock.Id);
                foreach (var asset in blockAssets)
                {
                    block.Assets.Add(asset);
                }

                section.Blocks.Add(block);
            }

            sectionList.Add(section);
        }

        // Fix parent IDs
        foreach (var stagedSection in sections.Where(s => s.ParentId.HasValue))
        {
            var section = sectionList.FirstOrDefault(s => s.Id == sectionIdMap[stagedSection.Id]);
            if (section != null)
            {
                section.ParentId = sectionIdMap[stagedSection.ParentId!.Value];
            }
        }

        return new ImportResult
        {
            Success = true,
            Document = document,
            Sections = sectionList,
            Warnings = [],
            Statistics = new ImportStatistics
            {
                TotalElementsParsed = stats?.TotalElementsParsed ?? 0,
                SectionsCreated = sectionList.Count,
                BlocksCreated = sectionList.Sum(s => s.Blocks.Count),
                EquationsFound = stats?.EquationsFound ?? 0,
                EquationsConverted = stats?.EquationsConverted ?? 0,
                ImagesExtracted = stats?.ImagesExtracted ?? 0,
                TablesExtracted = stats?.TablesExtracted ?? 0,
                CodeBlocksDetected = stats?.CodeBlocksDetected ?? 0
            }
        };
    }

    /// <summary>
    /// Cleans up old sessions (older than specified days).
    /// </summary>
    public int CleanupOldSessions(int daysOld = 30)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            DELETE FROM import_session
            WHERE status IN ('cancelled', 'accepted')
            AND modified_at < @cutoff_date;
            """;
        cmd.Parameters.AddWithValue("@cutoff_date", DateTime.UtcNow.AddDays(-daysOld).ToString("O"));
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets block counts by type for a session.
    /// </summary>
    public Dictionary<BlockType, int> GetBlockCountsByType(int sessionId)
    {
        var counts = new Dictionary<BlockType, int>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT block_type, COUNT(*) FROM staged_block
            WHERE session_id = @session_id AND is_deleted = 0
            GROUP BY block_type;
            """;
        cmd.Parameters.AddWithValue("@session_id", sessionId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var blockType = Enum.Parse<BlockType>(reader.GetString(0));
            counts[blockType] = reader.GetInt32(1);
        }
        return counts;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _connection?.Close();
        _connection?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Represents a staged import session.
/// </summary>
public class StagedSession
{
    public int Id { get; set; }
    public string SourceFile { get; set; } = "";
    public string SourceFormat { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
}

/// <summary>
/// Represents a staged section.
/// </summary>
public class StagedSection
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>
/// Represents a staged block.
/// </summary>
public class StagedBlock
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public BlockType BlockType { get; set; }
    public string Content { get; set; } = "";
    public int SortOrder { get; set; }
    public BlockType OriginalBlockType { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsModified { get; set; }
}

/// <summary>
/// Represents a staged issue.
/// </summary>
public class StagedIssue
{
    public int Id { get; set; }
    public string Severity { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Details { get; set; } = "";
    public int? AffectedSectionId { get; set; }
    public int? AffectedBlockId { get; set; }
    public bool IsResolved { get; set; }
}

/// <summary>
/// Represents staged statistics.
/// </summary>
public class StagedStatistics
{
    public int TotalElementsParsed { get; set; }
    public int SectionsCreated { get; set; }
    public int BlocksCreated { get; set; }
    public int EquationsFound { get; set; }
    public int EquationsConverted { get; set; }
    public int ImagesExtracted { get; set; }
    public int TablesExtracted { get; set; }
    public int CodeBlocksDetected { get; set; }
    public int ParseTimeMs { get; set; }
    public int ConvertTimeMs { get; set; }
}

/// <summary>
/// Represents a staged asset (without binary data for listing).
/// </summary>
public class StagedAsset
{
    public int Id { get; set; }
    public string Filename { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
