using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Intelibill.Integration.Tests;

/// <summary>Request/operation-local observations; never captures SQL parameter values.</summary>
internal sealed class ReadQueryProbe : DbCommandInterceptor
{
    private static readonly string[] BusinessTables = ["sales", "sale_items", "sale_returns", "sale_return_items", "sale_return_settlements", "sale_cancellations", "sale_cancellation_lines", "credit_notes", "items", "customers", "suppliers", "customer_ledger_entries", "supplier_ledger_entries", "discount_rules"];
    private readonly AsyncLocal<Observation?> _current = new();
    private readonly ConcurrentDictionary<string, Observation> _requests = new();
    public Func<string?>? RequestIdentity { get; set; }
    public const string Header = "X-Test-Read-Probe";

    public Observation Observe()
    {
        var id = Guid.NewGuid().ToString("N");
        var observation = new Observation(id, () => { _current.Value = null; _requests.TryRemove(id, out _); });
        _requests[id] = observation;
        _current.Value = observation;
        return observation;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        var observation = _current.Value;
        if (observation is null && RequestIdentity?.Invoke() is { } id) _requests.TryGetValue(id, out observation);
        if (observation is null || !IsBusinessRead(command.CommandText)) return ValueTask.FromResult(result);
        var read = new Read(command.CommandText);
        observation.Reads.Enqueue(read);
        return ValueTask.FromResult<DbDataReader>(new CountingReader(result, read, observation));
    }

    private static bool IsBusinessRead(string sql) => sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
        && BusinessTables
            .Any(table => sql.Contains($"FROM {table} ", StringComparison.Ordinal) || sql.Contains($"JOIN {table} ", StringComparison.Ordinal));

    internal sealed class Read(string sql)
    {
        public string Sql { get; } = sql;
        public int Rows { get; internal set; }
        public bool IsAggregate => Sql.Contains("count(", StringComparison.OrdinalIgnoreCase) || Sql.Contains("sum(", StringComparison.OrdinalIgnoreCase) || Sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class Observation(string id, Action dispose) : IDisposable
    {
        public string Id { get; } = id;
        public ConcurrentQueue<Read> Reads { get; } = new();
        public Func<Read, Task>? AfterRead { get; set; }
        public void Dispose() => dispose();
    }

    private sealed class CountingReader(DbDataReader inner, Read read, Observation observation) : DbDataReader
    {
        private bool _completed;
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            var found = await inner.ReadAsync(cancellationToken);
            if (found) read.Rows++;
            else await CompleteAsync();
            return found;
        }
        private async Task CompleteAsync()
        {
            if (_completed) return;
            _completed = true;
            if (observation.AfterRead is { } afterRead) await afterRead(read);
        }
        public override bool Read()
        {
            var found = inner.Read();
            if (found) read.Rows++;
            else CompleteAsync().GetAwaiter().GetResult();
            return found;
        }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); await CompleteAsync(); await base.DisposeAsync(); GC.SuppressFinalize(this); }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Close() => inner.Close();
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => inner.IsDBNullAsync(ordinal, cancellationToken);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override DataTable? GetSchemaTable() => inner.GetSchemaTable();
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
    }
}
