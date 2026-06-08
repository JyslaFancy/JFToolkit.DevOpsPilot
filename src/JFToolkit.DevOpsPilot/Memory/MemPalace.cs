using System.Text;
using Microsoft.Data.Sqlite;

namespace JFToolkit.DevOpsPilot.Memory;

/// <summary>
/// SQLite-backed persistent memory for chat sessions and project knowledge.
/// Cross-session recall with FTS5 full-text search.
/// Zero external dependencies — uses built-in Microsoft.Data.Sqlite.
/// </summary>
public sealed class MemPalace : IDisposable
{
    private readonly SqliteConnection _db;
    private bool _initialized;

    public MemPalace(string? dbPath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jftoolkit");
        Directory.CreateDirectory(dir);

        dbPath ??= Path.Combine(dir, "mempalace.db");
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        _db = new SqliteConnection(csb.ConnectionString);
        _db.Open();

        // Enable foreign key enforcement (required for REFERENCES to work)
        Execute("PRAGMA foreign_keys = ON");

        InitSchema();
    }

    // ── Schema ──

    private void InitSchema()
    {
        if (_initialized) return;

        using var tx = _db.BeginTransaction();

        ExecuteInTx(tx, @"
            CREATE TABLE IF NOT EXISTS sessions (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                project     TEXT NOT NULL,
                title       TEXT,
                created_at  TEXT NOT NULL DEFAULT (datetime('now')),
                message_count INTEGER DEFAULT 0
            )");

        ExecuteInTx(tx, @"
            CREATE TABLE IF NOT EXISTS messages (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id  INTEGER NOT NULL REFERENCES sessions(id),
                seq         INTEGER NOT NULL,
                role        TEXT NOT NULL,
                content     TEXT NOT NULL,
                timestamp   TEXT NOT NULL DEFAULT (datetime('now'))
            )");

        ExecuteInTx(tx, @"
            CREATE TABLE IF NOT EXISTS project_memory (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                project     TEXT NOT NULL,
                key         TEXT NOT NULL,
                value       TEXT NOT NULL,
                created_at  TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at  TEXT NOT NULL DEFAULT (datetime('now')),
                UNIQUE(project, key)
            )");

        // FTS5 for full-text search on messages
        ExecuteInTx(tx, @"
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                content,
                content='messages',
                content_rowid='id'
            )");

        // Triggers to keep FTS5 in sync
        ExecuteInTx(tx, @"
            CREATE TRIGGER IF NOT EXISTS messages_ai AFTER INSERT ON messages BEGIN
                INSERT INTO messages_fts(rowid, content) VALUES (new.id, new.content);
            END");

        ExecuteInTx(tx, @"
            CREATE TRIGGER IF NOT EXISTS messages_ad AFTER DELETE ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, content) VALUES ('delete', old.id, old.content);
            END");

        ExecuteInTx(tx, @"
            CREATE TRIGGER IF NOT EXISTS messages_au AFTER UPDATE ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, content) VALUES ('delete', old.id, old.content);
                INSERT INTO messages_fts(rowid, content) VALUES (new.id, new.content);
            END");

        // Indexes
        ExecuteInTx(tx, "CREATE INDEX IF NOT EXISTS idx_messages_session ON messages(session_id)");
        ExecuteInTx(tx, "CREATE INDEX IF NOT EXISTS idx_project_memory_project ON project_memory(project)");
        ExecuteInTx(tx, "CREATE INDEX IF NOT EXISTS idx_sessions_project ON sessions(project)");

        tx.Commit();

        // Rebuild FTS5 index to fix any stale/corrupt entries
        // (prevents SQLITE_CONSTRAINT from duplicate rowids in messages_fts)
        try { Execute("INSERT INTO messages_fts(messages_fts) VALUES('rebuild')"); }
        catch { /* best-effort — non-critical if FTS is already clean */ }

        _initialized = true;
    }

    // ── Session Management ──

    public int CreateSession(string project, string? title = null)
    {
        title ??= $"Chat — {DateTime.Now:yyyy-MM-dd HH:mm}";
        using var cmd = new SqliteCommand(
            "INSERT INTO sessions (project, title) VALUES (@p, @t); SELECT last_insert_rowid();",
            _db);
        cmd.Parameters.AddWithValue("@p", project);
        cmd.Parameters.AddWithValue("@t", title);
        var id = (long)cmd.ExecuteScalar()!;
        return (int)id;
    }

    public void SaveMessage(int sessionId, int seq, string role, string content)
    {
        try
        {
            InsertMessage(sessionId, seq, role, content);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            // FTS5 index is likely stale — rebuild and retry once
            try
            {
                Execute("INSERT INTO messages_fts(messages_fts) VALUES('rebuild')");
                InsertMessage(sessionId, seq, role, content);
            }
            catch (SqliteException retryEx)
            {
                throw new InvalidOperationException(
                    $"SQLite constraint violation persists after FTS5 rebuild. " +
                    $"Session={sessionId}, Seq={seq}, Role={role}. " +
                    $"Inner: {retryEx.Message}", ex);
            }
        }
    }

    private void InsertMessage(int sessionId, int seq, string role, string content)
    {
        using var cmd = new SqliteCommand(
            "INSERT INTO messages (session_id, seq, role, content) VALUES (@s, @q, @r, @c)",
            _db);
        cmd.Parameters.AddWithValue("@s", sessionId);
        cmd.Parameters.AddWithValue("@q", seq);
        cmd.Parameters.AddWithValue("@r", role);
        cmd.Parameters.AddWithValue("@c", content);
        cmd.ExecuteNonQuery();

        // Update message count
        using var upd = new SqliteCommand(
            "UPDATE sessions SET message_count = message_count + 1 WHERE id = @id",
            _db);
        upd.Parameters.AddWithValue("@id", sessionId);
        upd.ExecuteNonQuery();
    }

    /// <summary>
    /// Load the most recent N messages for a project across all sessions.
    /// </summary>
    public List<ChatMessage> LoadRecentMessages(string project, int count = 20)
    {
        var messages = new List<ChatMessage>();
        using var cmd = new SqliteCommand(@"
            SELECT m.role, m.content
            FROM messages m
            JOIN sessions s ON m.session_id = s.id
            WHERE s.project = @p
            ORDER BY m.id DESC
            LIMIT @c", _db);
        cmd.Parameters.AddWithValue("@p", project);
        cmd.Parameters.AddWithValue("@c", count);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(new ChatMessage(
                reader.GetString(0),
                reader.GetString(1)));
        }
        messages.Reverse(); // Oldest first
        return messages;
    }

    /// <summary>
    /// Search messages across all sessions using FTS5.
    /// </summary>
    public List<SearchHit> SearchMessages(string query, int limit = 20)
    {
        var hits = new List<SearchHit>();
        using var cmd = new SqliteCommand(@"
            SELECT s.project, s.title, m.role, m.content, s.created_at
            FROM messages_fts f
            JOIN messages m ON f.rowid = m.id
            JOIN sessions s ON m.session_id = s.id
            WHERE messages_fts MATCH @q
            ORDER BY rank
            LIMIT @l", _db);
        cmd.Parameters.AddWithValue("@q", query);
        cmd.Parameters.AddWithValue("@l", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            hits.Add(new SearchHit(
                reader.GetString(0),  // project
                reader.GetString(1),  // session title
                reader.GetString(2),  // role
                reader.GetString(3),  // content
                reader.GetString(4)   // created_at
            ));
        }
        return hits;
    }

    /// <summary>
    /// List recent sessions for a project.
    /// </summary>
    public List<SessionSummary> ListSessions(string project, int limit = 10)
    {
        var sessions = new List<SessionSummary>();
        using var cmd = new SqliteCommand(@"
            SELECT id, title, message_count, created_at
            FROM sessions
            WHERE project = @p
            ORDER BY created_at DESC
            LIMIT @l", _db);
        cmd.Parameters.AddWithValue("@p", project);
        cmd.Parameters.AddWithValue("@l", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new SessionSummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3)));
        }
        return sessions;
    }

    // ── Project Memory (key-value facts) ──

    public void Remember(string project, string key, string value)
    {
        using var cmd = new SqliteCommand(@"
            INSERT INTO project_memory (project, key, value, updated_at)
            VALUES (@p, @k, @v, datetime('now'))
            ON CONFLICT(project, key) DO UPDATE SET
                value = @v,
                updated_at = datetime('now')", _db);
        cmd.Parameters.AddWithValue("@p", project);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    public string? Recall(string project, string key)
    {
        using var cmd = new SqliteCommand(
            "SELECT value FROM project_memory WHERE project = @p AND key = @k",
            _db);
        cmd.Parameters.AddWithValue("@p", project);
        cmd.Parameters.AddWithValue("@k", key);
        var result = cmd.ExecuteScalar();
        return result?.ToString();
    }

    public void Forget(string project, string key)
    {
        using var cmd = new SqliteCommand(
            "DELETE FROM project_memory WHERE project = @p AND key = @k",
            _db);
        cmd.Parameters.AddWithValue("@p", project);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, string> GetAllMemories(string project)
    {
        var mems = new Dictionary<string, string>();
        using var cmd = new SqliteCommand(
            "SELECT key, value FROM project_memory WHERE project = @p ORDER BY key",
            _db);
        cmd.Parameters.AddWithValue("@p", project);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            mems[reader.GetString(0)] = reader.GetString(1);
        return mems;
    }

    /// <summary>
    /// Build a context string for injection into the system prompt.
    /// Formats project memories as bullet points.
    /// </summary>
    public string BuildMemoryContext(string project)
    {
        var mems = GetAllMemories(project);
        if (mems.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("You know the following about this project from past sessions:");
        foreach (var (key, value) in mems)
            sb.AppendLine($"  - {key}: {value}");
        sb.AppendLine();
        return sb.ToString();
    }

    // ── Helpers ──

    private void Execute(string sql)
    {
        using var cmd = new SqliteCommand(sql, _db);
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteInTx(SqliteTransaction tx, string sql)
    {
        using var cmd = new SqliteCommand(sql, tx.Connection, tx);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _db?.Close();
        _db?.Dispose();
    }
}

// ── Data Types ──

public record ChatMessage(string Role, string Content);

public record SearchHit(
    string Project,
    string SessionTitle,
    string Role,
    string Content,
    string CreatedAt);

public record SessionSummary(
    int Id,
    string Title,
    int MessageCount,
    string CreatedAt);
