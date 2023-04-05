// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace TheUtils.UnitTests;

using System.Linq.Expressions;
using AutoFixture;
using AutoFixture.Dsl;
using Moq;
using Moq.AutoMock;
using Moq.Language.Flow;

public abstract class UnitTestBase<TSystemUnderTest> where TSystemUnderTest : class
{
    readonly AutoMocker _mocker;
    readonly Lazy<TSystemUnderTest> _sut;
    readonly Fixture _fixture;

    protected UnitTestBase()
    {
        _fixture = new Fixture();
        _mocker = new AutoMocker();
        _sut = new Lazy<TSystemUnderTest>(InstanceFactory);

        CancelToken = new CancellationTokenSource().Token;
    }

    protected virtual TSystemUnderTest InstanceFactory() => _mocker.CreateInstance<TSystemUnderTest>();

    // shortcuts to predefined state
    protected AutoMocker Mocker => _mocker;
    protected Fixture Fixture => _fixture;
    protected TSystemUnderTest SystemUnderTest => _sut.Value;
    protected CancellationToken CancelToken { get; }

    // AutoFixture helpers
    protected ICustomizationComposer<T> Build<T>() => _fixture.Build<T>();
    protected T Create<T>() => _fixture.Create<T>();

    // type-inference helpers
    protected Expression<Func<T, TR>> MethodCall<T, TR>(Expression<Func<T, TR>> expression) => expression;
    protected Expression<Action<T>> MethodCall<T>(Expression<Action<T>> expression) => expression;

    // setup usability helpers
    protected Mock<T> Mock<T>() where T : class => _mocker.GetMock<T>();

    protected void Use<T>(T service) => _mocker.Use(service);

    protected ISetup<T, TR> Setup<T, TR>(Expression<Func<T, TR>> expression) where T : class =>
        Mock<T>().Setup(expression);

    protected ISetup<T> Setup<T>(Expression<Action<T>> expression) where T : class =>
        Mock<T>().Setup(expression);

    // verification usability helpers
    protected void Verify<T, TResult>(Expression<Func<T, TResult>> expression, Func<Times> times) where T : class =>
        Mock<T>().Verify(expression, times);

    protected void Verify<T>(Expression<Action<T>> expression, Func<Times> times) where T : class =>
        Mock<T>().Verify(expression, times);
}