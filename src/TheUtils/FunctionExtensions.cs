namespace TheUtils;

using static Functions;

public static class FunctionsExtensions
{
    public static async ValueTask<TOutput> InvokeUnsafe<TInput, TOutput>
    (
        this IFunctionAff<TInput, TOutput> func,
        TInput input,
        CancellationToken token
    )
    {
        var result = await func.Invoke(input, token).Run();
        return result.ThrowIfFail();
    }

    public static async ValueTask<TOutput> InvokeUnsafe<TInput, TOutput>
    (
        this FunctionAsync<TInput, TOutput> func,
        TInput input,
        CancellationToken token
    )
    {
        var result = await func.Invoke(input, token).Run();
        return result.ThrowIfFail();
    }

    public static TOutput InvokeUnsafe<TInput, TOutput>
    (
        this IFunctionEff<TInput, TOutput> func,
        TInput input,
        CancellationToken token
    ) =>
        func.Invoke(input).Run().ThrowIfFail();

    public static TOutput InvokeUnsafe<TInput, TOutput>
    (
        this Function<TInput, TOutput> func,
        TInput input,
        CancellationToken token
    ) =>
        func.Invoke(input).Run().ThrowIfFail();
}