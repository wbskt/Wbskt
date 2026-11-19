namespace Wbskt.Infrastructure.Csv;

/// <summary>
/// A CSV file to stream: its header line and its rows, each without a line ending, read only as they
/// are written. <see cref="Rows"/> may yield one row more than <see cref="MaxRows"/>; the extra row is
/// not written, it only says the file was cut.
/// </summary>
public sealed record CsvExport(string Header, IAsyncEnumerable<string> Rows, int MaxRows);
