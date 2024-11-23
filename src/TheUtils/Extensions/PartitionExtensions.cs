// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils.Extensions;

using LanguageExt;
using static LanguageExt.Prelude;

public static partial class TheUtilsExtensions
{
    public static Seq<Seq<T>> Partition<T>(this IEnumerable<T> seq, int size) =>
        partition(seq, size);

    public static Seq<Seq<T>> partition<T>(IEnumerable<T> seq, int size) =>
        seq.Chunk(size).AsIterable().Map(toSeq).ToSeq();
}