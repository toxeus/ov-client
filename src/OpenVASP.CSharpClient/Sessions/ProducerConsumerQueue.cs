using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenVASP.Messaging;
using OpenVASP.Messaging.Messages;

[assembly: InternalsVisibleTo("OpenVASP.Tests")]

namespace OpenVASP.CSharpClient.Sessions
{
    internal class ProducerConsumerQueue : IDisposable
    {
        private readonly MessageHandlerResolver _messageHandlerResolver;
        private readonly ConcurrentQueue<MessageBase> _bufferQueue = new ConcurrentQueue<MessageBase>();
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly AutoResetEvent _manual = new AutoResetEvent(false);

        private Task _queueWorker;

        public ProducerConsumerQueue(MessageHandlerResolver messageHandlerResolver, CancellationToken cancellationToken)
        {
            _messageHandlerResolver = messageHandlerResolver;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            StartWorker();
        }

        public void Enqueue(MessageBase message)
        {
            _bufferQueue.Enqueue(message);

            _manual.Set();
        }

        private void StartWorker()
        {
            var cancellationToken = _cancellationTokenSource.Token;
            var factory = new TaskFactory();
            _queueWorker = factory.StartNew(async _ =>
            {
                do
                {
                    _manual.WaitOne();

                    while (_bufferQueue.Any())
                    {
                        try
                        {
                            if (!_bufferQueue.TryDequeue(out var item))
                                continue;

                            var handlers = _messageHandlerResolver.ResolveMessageHandlers(item.GetType());

                            foreach (var handler in handlers)
                            {
                                await handler.HandleMessageAsync(item, cancellationToken);
                            }
                        }
                        catch (Exception e)
                        {
                            //TODO: Add logging here
                            throw;
                        }
                    }

                } while (!cancellationToken.IsCancellationRequested);

            }, cancellationToken, TaskCreationOptions.LongRunning);
        }

        public void Dispose()
        {
            _manual.Set();
            _cancellationTokenSource.Cancel();

            try
            {
                _queueWorker.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                //TODO: log exception
                // ignored
            }

            _manual?.Dispose();
            _queueWorker?.Dispose();
        }
    }
}
