using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

namespace Logging
{
    [TestFixture]
    public class ContextLoggerTests
    {
        [Test]
        public async Task Should_Add_Lost_Scopes_When_Logging_Exceptions()
        {
            var lastStates = new Stack<string>();
            var disposable = Substitute.For<IDisposable>();
            var logger = Substitute.For<ILogger<ContextLoggerTests>>();
            logger.BeginScope(Arg.Any<string>()).Returns(ci =>
            {
                lastStates.Push(ci.ArgAt<string>(0));
                return disposable;
            });
            var contextLogger = new ContextLogger<ContextLoggerTests>(logger);

            contextLogger.LogTrace("x");
            
            using (contextLogger.BeginScope("a"))
            {
                lastStates.Count.ShouldBe(1);
                lastStates.Pop().ShouldBe("a");
                
                contextLogger.LogTrace("a");
                try
                {
                     await AsyncMethod1(contextLogger, lastStates);
                }
                catch (Exception exception)
                {
                    contextLogger.LogTrace("exception");
                    
                    contextLogger.LogError(new Exception(), "message");
                    lastStates.Count.ShouldBe(0);

                    contextLogger.LogError(exception, "message");
                    lastStates.Count.ShouldBe(2);
                    lastStates.Pop().ShouldBe("c");
                    lastStates.Pop().ShouldBe("b");
                }
            }
        }

        private static async Task AsyncMethod1(ContextLogger<ContextLoggerTests> contextLogger, Stack<string> lastStates)
        {
            await Task.Delay(1);
            
            using (contextLogger.BeginScope("b"))
            {
                lastStates.Count.ShouldBe(1);
                lastStates.Pop().ShouldBe("b");
                contextLogger.LogTrace("b");
                
                await Task.Delay(1);
                
                using (contextLogger.BeginScope("c"))
                {
                    lastStates.Count.ShouldBe(1);
                    lastStates.Pop().ShouldBe("c");
                    contextLogger.LogTrace("c");
                    
                    await Task.Delay(1);
                    
                    try
                    {
                        using (contextLogger.BeginScope("d"))
                        {
                            lastStates.Count.ShouldBe(1);
                            lastStates.Pop().ShouldBe("d");

                            await Task.Delay(1);
                            
                            contextLogger.LogTrace("d");
                            
                            throw new Exception();
                        }
                    }
                    catch (Exception)
                    {
                    }

                    throw new Exception();
                }
            }
        }
    }
}
