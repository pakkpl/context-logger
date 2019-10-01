using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Logging
{
    internal class ContextLoggerState
    {
        internal static readonly AsyncLocal<ContextLoggerState> AsyncLocal = new AsyncLocal<ContextLoggerState>();

        internal readonly Stack<object> States = new Stack<object>();

        internal Exception LastException;
        internal Stack<object> LastExceptionStates;
    }

    public class ContextLogger<T> : ILogger<T>, IDisposable
    {
        private readonly ILogger<T> _logger;

        public ContextLogger(ILogger<T> logger)
        {
            _logger = logger;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomainOnFirstChanceException;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.FirstChanceException -= CurrentDomainOnFirstChanceException;
        }

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            IDisposable[] scopes = null;

            if (exception != null)
            {
                var asyncLocalValue = ContextLoggerState.AsyncLocal.Value;
                if (asyncLocalValue != null && exception == asyncLocalValue.LastException)
                {
                    var lastExceptionStates = asyncLocalValue.LastExceptionStates;
                    var missingStatesCount = lastExceptionStates.Count - asyncLocalValue.States.Count;
                    if (missingStatesCount > 0)
                    {
                        scopes = new IDisposable[missingStatesCount];
                        var missingStatesOffset = lastExceptionStates.Count - missingStatesCount;
                        for (var i = 0; i < missingStatesCount; i++)
                        {
                            scopes[i] = _logger.BeginScope(lastExceptionStates.ElementAt(missingStatesOffset + i));
                        }
                    }
                }
            }

            _logger.Log(logLevel, eventId, state, exception, formatter);

            if (scopes == null)
            {
                return;
            }
            
            foreach (var scope in scopes)
            {
                scope.Dispose();
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return new Scope(_logger.BeginScope(state), state);
        }

        private class Scope : IDisposable
        {
            private IDisposable _disposable;

            public Scope(IDisposable disposable, object state)
            {
                _disposable = disposable;
                if (ContextLoggerState.AsyncLocal.Value == null)
                {
                    ContextLoggerState.AsyncLocal.Value = new ContextLoggerState();
                }

                ContextLoggerState.AsyncLocal.Value.States.Push(state);
            }

            public void Dispose()
            {
                if (_disposable == null)
                {
                    return;
                }

                _disposable.Dispose();
                _disposable = null;

                ContextLoggerState.AsyncLocal.Value.States.Pop();
            }
        }

        private static void CurrentDomainOnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            var asyncLocalValue = ContextLoggerState.AsyncLocal.Value;
            if (asyncLocalValue == null || asyncLocalValue.LastException == e.Exception)
            {
                return;
            }

            if (!asyncLocalValue.States.Any())
            {
                asyncLocalValue.LastException = null;
                asyncLocalValue.LastExceptionStates = null;
                return;
            }

            asyncLocalValue.LastException = e.Exception;

            if (asyncLocalValue.LastExceptionStates == null)
            {
                asyncLocalValue.LastExceptionStates = new Stack<object>();
            }
            else
            {
                asyncLocalValue.LastExceptionStates.Clear();
            }

            foreach (var scope in asyncLocalValue.States)
            {
                asyncLocalValue.LastExceptionStates.Push(scope);
            }
        }
    }
}
