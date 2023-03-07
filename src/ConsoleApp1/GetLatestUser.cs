namespace ConsoleApp1;

using FluentValidation;
using LanguageExt;
using TheUtils;
using static LanguageExt.Prelude;
using static TheUtils.Functions;

public static partial class Database
{
    [GenerateDelegates]
    public partial class GetLatestUser : FunctionAff<GetLatestUser.InputType, GetLatestUser.ResultType>
    {
        public readonly record struct InputType
        (
            string UserName,
            int Id
        );

        public readonly record struct ResultType
        (
            int UserCount
        );

        protected override InputValidator<InputType> Validator { get; } = v =>
        {
            v.RuleFor(x => x.Id).GreaterThan(0);
            v.RuleFor(x => x.UserName).NotEmpty();
        };

        public GetLatestUser( /* dependencies */)
        {
        }

        protected override Aff<ResultType> InvokeAff(InputType input) =>
            // SuccessAff("hello");
            SuccessAff(new ResultType(_token.GetHashCode()));
    }
}

// [GenerateDelegates]
// public partial class DeleteUser : FunctionAff<GetLatestUser.ResultType>
// {
//     protected override Aff<GetLatestUser.ResultType> InvokeAff()
//     {
//         throw new NotImplementedException();
//     }
// }

[GenerateDelegates]
public partial class StartJob : FunctionAff
{
    protected override Aff<Unit> InvokeAff()
    {
        throw new NotImplementedException();
    }
}