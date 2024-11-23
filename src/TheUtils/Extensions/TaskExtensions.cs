namespace TheUtils.Extensions;

using LanguageExt;
using static LanguageExt.Prelude;

public static partial class TheUtilsExtensions
{
    public static async Task<Seq<T>> ToSeq<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken token
    ) => toSeq(await source.ToListAsync(cancellationToken: token).ConfigureAwait(false));

    public static async Task<Seq<T>> ToSeq<T>(this Task<List<T>> source) =>
        toSeq(await source.ConfigureAwait(false));

    public static async Task<Seq<T>> ToSeq<T>(this Task<IReadOnlyList<T>> source) =>
        toSeq(await source.ConfigureAwait(false));

    public static async Task<Seq<T>> ToSeq<T>(this Task<T[]> source) =>
        toSeq(await source.ConfigureAwait(false));

    public static async Task<Seq<T>> ToSeq<T>(this Task<IEnumerable<T>> source) =>
        toSeq(await source.ConfigureAwait(false));
}