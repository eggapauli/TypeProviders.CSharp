using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace TypeProviders.CSharp.Test
{
    public class MatchTest
    {
        public static IEnumerable<object[]> MatchInputObjects
        {
            get
            {
                yield return new object[] { new D(), 1 };
                yield return new object[] { new C(), 2 };
                yield return new object[] { new B(), 3 };
                yield return new object[] { new A(), 4 };
                yield return new object[] { new Base(), 5 };
            }
        }

        [Theory]
        [MemberData(nameof(MatchInputObjects))]
        public void ShouldMatchObjects(object input, int expectedOutput)
        {
            new Match<int>()
                .With((D x) => 1)
                .With((C x) => 2)
                .With((B x) => 3)
                .With((A x) => 4)
                .With((Base x) => 5)
                .Run(input)
                .Should().Be(expectedOutput);
        }

        [Fact]
        public void ShouldThrowWhenNoHandlersAreAdded()
        {
            var sut = new Match<int>();
            new Action(() => sut.Run(new object()))
                .ShouldThrow<MatchException>();
        }

        [Fact]
        public void ShouldWorkWithDerivedTypes()
        {
            var match = new Match<int>()
                .With((Base x) => 1);
            new Action(() => match.Run(new D()))
                .ShouldNotThrow();
        }

        [Fact]
        public void ShouldThrowWhenObjectCantBeHandled()
        {
            var sut = new Match<int>()
                .With((Base x) => 1);
            new Action(() => sut.Run(new object()))
                .ShouldThrow<MatchException>();
        }

        class Base { }
        class A : Base { }
        class B : A { }
        class C : Base { }
        class D : C { }
    }
}
