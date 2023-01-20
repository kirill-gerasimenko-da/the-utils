// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;

public static class PartitionExtensions
{
    public static IEnumerable<Seq<T>> Partition<T>(this IEnumerable<T> seq, int size) =>
        partition(seq, size);

    public static Seq<Seq<T>> Partition<T>(this Seq<T> seq, int size) =>
        partition(seq, size);

    public static IEnumerable<Seq<T>> partition<T>(IEnumerable<T> seq, int size) =>
        seq.Chunk(size).Map(chunk => chunk.ToSeq());

    public static Seq<Seq<T>> partition<T>(Seq<T> seq, int size) =>
        seq.Chunk(size).Map(chunk => chunk.ToSeq()).ToSeq();
}