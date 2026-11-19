using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Wbskt.Infrastructure.Csv;

/// <summary>
/// Writes a <see cref="CsvExport"/> to the response as it is read, so an export of many rows holds one
/// row in memory rather than the whole file several times over.
/// </summary>
/// <remarks>
/// The status and headers go out before the first row is read, so a file cut at its limit can't say so
/// in a header. It ends with a <see cref="TruncatedPrefix"/> line instead, which a spreadsheet shows as
/// the last row and a script can look for.
/// </remarks>
public sealed class CsvFileResult : IActionResult
{
    public const string TruncatedPrefix = "# truncated at ";

    private const int BufferSize = 16 * 1024;

    private readonly string _fileName;
    private readonly CsvExport _export;

    public CsvFileResult(string fileName, CsvExport export)
    {
        _fileName = fileName;
        _export = export;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = "text/csv; charset=utf-8";
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(_fileName);
        response.Headers.ContentDisposition = disposition.ToString();

        await WriteAsync(response.Body, _export, context.HttpContext.RequestAborted);
    }

    /// <summary>Writes the header and the rows to <paramref name="body"/>; returns how many rows were written.</summary>
    public static async Task<int> WriteAsync(Stream body, CsvExport export, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), BufferSize, leaveOpen: true);
        await writer.WriteAsync(export.Header);
        await writer.WriteAsync("\r\n");

        int written = 0;
        await foreach (var row in export.Rows.WithCancellation(cancellationToken))
        {
            if (written == export.MaxRows)
            {
                await writer.WriteAsync(string.Create(CultureInfo.InvariantCulture, $"{TruncatedPrefix}{export.MaxRows} rows; export a shorter range\r\n"));
                break;
            }

            await writer.WriteAsync(row);
            await writer.WriteAsync("\r\n");
            written++;
        }

        await writer.FlushAsync(cancellationToken);
        return written;
    }
}
