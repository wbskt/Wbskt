namespace Wbskt.Common.Extensions;

public static class ProviderExtensions
{
    public static object? ReplaceDbNulls(object? value)
    {
        if (value is DateTime time && time == DateTime.UnixEpoch)
        {
            return DBNull.Value;
        }

        if (value == null)
        {
            return DBNull.Value;
        }

        if (value == DBNull.Value)
        {
            return null;
        }

        return value;
    }
}
