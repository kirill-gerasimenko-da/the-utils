namespace TheUtils;

using static LanguageExt.Prelude;
using LanguageExt;

public static class SeqExtensions
{
    public static async Task<Seq<T>> ToSeq<T>(this IAsyncEnumerable<T> source, CancellationToken token) =>
        toSeq(await source.ToListAsync(cancellationToken: token));

    public static async Task<Seq<T>> ToSeq<T>(this Task<List<T>> source) => toSeq(await source);
    public static async Task<Seq<T>> ToSeq<T>(this Task<IReadOnlyList<T>> source) => toSeq(await source);
    public static async Task<Seq<T>> ToSeq<T>(this Task<T[]> source) => toSeq(await source);
}