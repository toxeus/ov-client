using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenVASP.CSharpClient.Events;
using OpenVASP.CSharpClient.Interfaces;
using OpenVASP.CSharpClient.Utils;
using OpenVASP.Messaging;
using OpenVASP.Messaging.Messages;
using OpenVASP.Messaging.Messages.Entities;
using Timer = System.Timers.Timer;

namespace OpenVASP.CSharpClient.Sessions
{
    //TODO: Add thread safety + state machine
    internal abstract class VaspSession : IDisposable
    {
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ISignService _signService;

        private bool _isActivated;
        private bool _hasReceivedTerminationMessage;
        private ProducerConsumerQueue _producerConsumerQueue;
        private Task _task;

        protected readonly MessageHandlerResolverBuilder _messageHandlerResolverBuilder;
        protected readonly ITransportClient _transportClient;

        protected string _sharedSymKeyId;
        
        public VaspSessionInfo Info { get; }

        public VaspSession(
            VaspSessionInfo vaspSessionInfo,
            ITransportClient transportClient,
            ISignService signService)
        {
            Info = vaspSessionInfo;
            _cancellationTokenSource = new CancellationTokenSource();
            _messageHandlerResolverBuilder = new MessageHandlerResolverBuilder();
            _transportClient = transportClient;
            _signService = signService;
        }

        protected void StartTopicMonitoring()
        {
            lock (_lock)
            {
                if (_isActivated)
                    throw new InvalidOperationException("Session was already started");

                var taskFactory = new TaskFactory(_cancellationTokenSource.Token);
                var cancellationToken = _cancellationTokenSource.Token;

                _task = taskFactory.StartNew(async _ =>
                {
                    var messageFilter = await _transportClient.CreateMessageFilterAsync(Info.Topic, symKeyId: _sharedSymKeyId);
                    _messageHandlerResolverBuilder.AddDefaultHandler(ProcessUnexpectedMessageAsync);
                    var messageHandlerResolver = _messageHandlerResolverBuilder.Build();
                    _producerConsumerQueue = new ProducerConsumerQueue(messageHandlerResolver, cancellationToken);

                    do
                    {
                        var messages = await _transportClient.GetSessionMessagesAsync(messageFilter);

                        if (messages == null || messages.Count == 0)
                        {
                            await Task.Delay(5000, cancellationToken);
                            continue;
                        }

                        foreach (var message in messages)
                        {
                            if (!_signService.VerifySign(
                                message.Payload,
                                message.Signature,
                                Info.CounterPartyPublicSigningKey))
                            {
                                //TODO: Log this
                                continue;
                            }

                            _producerConsumerQueue.Enqueue(message.Message);
                        }
                    } while (!cancellationToken.IsCancellationRequested);
                }, cancellationToken, TaskCreationOptions.LongRunning);

                _isActivated = true;
            }
        }

        public async Task WaitAsync()
        {
            try
            {
                await _task;
            }
            catch (Exception e)
            {
                // TODO log this
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();

            _task?.Dispose();
            _producerConsumerQueue?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        protected Task<string> RegisterSymKeyAsync()
        {
            return _transportClient.RegisterSymKeyAsync(Info.SharedEncryptionKey);
        }

        private Task ProcessUnexpectedMessageAsync(MessageBase message, CancellationToken token)
        {
            // TODO log this;

            return Task.CompletedTask;
        }
    }
}