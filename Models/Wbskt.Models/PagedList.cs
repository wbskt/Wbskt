namespace Wbskt.Models;

public interface IPagedList<out T> : IEnumerable<T>
{
    int TotalCount { get; }
}

public class PagedList<T> : List<T>, IPagedList<T>
{
    public PagedList(IEnumerable<T> items, int count)
    {
        TotalCount = count;
        AddRange(items);
    }

    public int TotalCount { get; }
}
