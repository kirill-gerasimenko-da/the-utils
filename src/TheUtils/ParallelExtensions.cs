// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

public static class ParallelExtensions
{
    public static async Task<(Seq<Error>, Seq<T>)> SequenceParallel<T>
    (
        this Seq<Task<T>> operations,
        int maxDegreeOfParallelism = 2
    ) =>
        await sequenceParallel(operations, maxDegreeOfParallelism);

    public static async Task<(Seq<Error>, Seq<T>)> SequenceParallel<T>
    (
        this Seq<Aff<T>> effects,
        int maxDegreeOfParallelism = 2
    ) =>
        await sequenceParallel(effects, maxDegreeOfParallelism);


    public static async Task<(Seq<Error>, Seq<T>)> sequenceParallel<T>
    (
        Seq<Task<T>> operations,
        int maxDegreeOfParallelism = 2
    )
    {
        if (maxDegreeOfParallelism <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

        var parallelOptions = new ParallelOptions {MaxDegreeOfParallelism = maxDegreeOfParallelism};
        var effects = operations.Map(o => o.ToAff());

        var effectResults = AtomSeq<Fin<T>>();

        await Parallel.ForEachAsync(effects, parallelOptions, async (effect, _) =>
            effectResults.Add(await effect.Run()));

        return effectResults.ToSeq().Partition();
    }

    public static async Task<(Seq<Error>, Seq<T>)> sequenceParallel<T>
    (
        Seq<Aff<T>> effects,
        int maxDegreeOfParallelism = 2
    )
    {
        if (maxDegreeOfParallelism <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

        var parallelOptions = new ParallelOptions {MaxDegreeOfParallelism = maxDegreeOfParallelism};
        var effectResults = AtomSeq<Fin<T>>();

        await Parallel.ForEachAsync(effects, parallelOptions, async (effect, _) =>
            effectResults.Add(await effect.Run()));

        return effectResults.ToSeq().Partition();
    }
}