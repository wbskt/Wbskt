using System.Data.Common;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

public class FakeSqlDataReader : DbDataReader
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;
    private int _currentRowIndex = -1;
    private IReadOnlyDictionary<string, object?>? _currentRow;

    private FakeSqlDataReader(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        _rows = rows;
    }

    public static FakeSqlDataReader From(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        return new FakeSqlDataReader(rows.ToList());
    }

    public override bool Read()
    {
        _currentRowIndex++;
        if (_currentRowIndex < _rows.Count)
        {
            _currentRow = _rows[_currentRowIndex];
            return true;
        }

        _currentRow = null;
        return false;
    }

    public override int GetOrdinal(string name)
    {
        if (_currentRow == null)
        {
            throw new InvalidOperationException("No current row. Call Read() first.");
        }

        var keys = _currentRow.Keys.ToList();
        var index = keys.IndexOf(name);
        if (index == -1)
        {
            throw new IndexOutOfRangeException($"Column '{name}' not found.");
        }

        return index;
    }

    public override object GetValue(int ordinal)
    {
        if (_currentRow == null)
        {
            throw new InvalidOperationException("No current row. Call Read() first.");
        }

        var key = _currentRow.Keys.ElementAt(ordinal);
        return _currentRow[key] ?? DBNull.Value;
    }

    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value == DBNull.Value || value == null;
    }

    public override int GetInt32(int ordinal)
    {
        return (int)GetValue(ordinal);
    }

    public override string GetString(int ordinal)
    {
        return (string)GetValue(ordinal);
    }

    public override Guid GetGuid(int ordinal)
    {
        return (Guid)GetValue(ordinal);
    }

    public override DateTime GetDateTime(int ordinal)
    {
        return (DateTime)GetValue(ordinal);
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = GetValue(ordinal);
        if (value is byte[] bytes)
        {
            if (buffer != null)
            {
                Array.Copy(bytes, (int)dataOffset, buffer, bufferOffset, length);
            }
            return bytes.Length;
        }

        throw new InvalidCastException("Column is not a byte array.");
    }

    public override int FieldCount => _currentRow?.Count ?? 0;

    public override bool HasRows => _rows.Count > 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override int Depth => throw new NotImplementedException();

    public override bool IsClosed => false;

    public override int RecordsAffected => -1;

    public override bool GetBoolean(int ordinal) => throw new NotImplementedException();

    public override byte GetByte(int ordinal) => throw new NotImplementedException();

    public override char GetChar(int ordinal) => throw new NotImplementedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotImplementedException();

    public override string GetDataTypeName(int ordinal) => throw new NotImplementedException();

    public override decimal GetDecimal(int ordinal) => throw new NotImplementedException();

    public override double GetDouble(int ordinal) => throw new NotImplementedException();

    public override Type GetFieldType(int ordinal) => throw new NotImplementedException();

    public override float GetFloat(int ordinal) => throw new NotImplementedException();

    public override short GetInt16(int ordinal) => throw new NotImplementedException();

    public override long GetInt64(int ordinal) => throw new NotImplementedException();

    public override string GetName(int ordinal) => _currentRow?.Keys.ElementAt(ordinal) ?? throw new InvalidOperationException();

    public override int GetValues(object[] values) => throw new NotImplementedException();

    public override bool NextResult() => false;

    public override System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
}