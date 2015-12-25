using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace TypeProviders.CSharp
{
    using MatchHandler = Tuple<Func<object, bool>, Func<object, object>>;

    public class Match<T>
    {
        IImmutableList<MatchHandler> _Handlers;

        public Match()
            : this(ImmutableList<MatchHandler>.Empty)
        {
        }

        Match(IImmutableList<MatchHandler> handlers)
        {
            _Handlers = handlers;
        }

        public Match<T> With<TIn>(Func<TIn, T> handler)
        {
            Func<object, bool> canExecute = obj =>
            {
                return typeof(TIn)
                    .GetTypeInfo()
                    .IsAssignableFrom(obj.GetType().GetTypeInfo());
            };
            Func<object, object> handlerFn = obj => handler((TIn)obj);
            return WithHandlers(_Handlers.Add(Tuple.Create(canExecute, handlerFn)));
        }

        public T Run(object obj)
        {
            try
            {
                return (T)_Handlers.First(h => h.Item1(obj)).Item2(obj);
            }
            catch (InvalidOperationException e)
            {
                throw new MatchException($"No handler found for input object {obj}", e);
            }
        }

        Match<T> WithHandlers(IImmutableList<MatchHandler> handlers)
        {
            return new Match<T>(handlers);
        }
    }

    public class MatchException : Exception
    {
        public MatchException() { }
        public MatchException(string message) : base(message) { }
        public MatchException(string message, Exception inner) : base(message, inner) { }
    }
}
