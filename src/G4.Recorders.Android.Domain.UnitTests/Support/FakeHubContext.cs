using G4.Recorders.Android.Domain.Hubs;

using Microsoft.AspNetCore.SignalR;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.UnitTests.Support
{
    /// <summary>
    /// In-memory <see cref="IHubContext{THub}"/> that records every send and lets a test play the recorder app.
    /// </summary>
    internal sealed class FakeHubContext : IHubContext<AndroidRecorderHub>
    {
        #region *** Fields       ***
        // The client proxies share one log and one callback.
        private readonly FakeHubClients _clients;
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes the context with no app simulation; tests set <see cref="OnSend"/> to play the app.
        /// </summary>
        public FakeHubContext()
        {
            _clients = new FakeHubClients(this);
        }
        #endregion

        #region *** Properties   ***
        /// <inheritdoc />
        public IHubClients Clients => _clients;

        /// <inheritdoc />
        public IGroupManager Groups => throw new NotSupportedException();

        /// <summary>
        /// Gets or sets the app simulation: receives the target, the method and the arguments of every send.
        /// </summary>
        public Action<string, string, object[]> OnSend { get; set; }

        /// <summary>
        /// Gets every send as target, method and arguments; the target is a connection id, a joined id list, or "all".
        /// </summary>
        public List<SentMessage> Sent { get; } = [];
        #endregion

        #region *** Methods      ***
        // Records one send on the owning context, then runs its simulation, which may complete repository requests.
        private static Task Send(FakeHubContext instance, SentMessage message)
        {
            instance.Sent.Add(message);
            instance.OnSend?.Invoke(message.Target, message.Method, message.Arguments);

            return Task.CompletedTask;
        }
        #endregion

        #region *** Nested Types ***
        // Hands out proxies bound to a target.
        private sealed class FakeHubClients(FakeHubContext owner) : IHubClients
        {
            public IClientProxy All => new FakeClientProxy(owner, "all");

            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy(owner, "all");

            public IClientProxy Client(string connectionId) => new FakeClientProxy(owner, connectionId);

            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeClientProxy(owner, string.Join(',', connectionIds));

            public IClientProxy Group(string groupName) => throw new NotSupportedException();

            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

            public IClientProxy User(string userId) => throw new NotSupportedException();

            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        // Forwards a send to the owner's log.
        private sealed class FakeClientProxy(FakeHubContext owner, string target) : IClientProxy
        {
            public Task SendCoreAsync(string method, object[] args, CancellationToken cancellationToken) => Send(instance: owner, message: new SentMessage(target, method, args));
        }
        #endregion
    }

    /// <summary>
    /// One message the code under test sent through <see cref="FakeHubContext"/>.
    /// </summary>
    /// <param name="Target">The connection id, a comma-joined id list, or "all".</param>
    /// <param name="Method">The client method name, such as <c>CloseRecorder</c>.</param>
    /// <param name="Arguments">The message arguments.</param>
    internal sealed record SentMessage(string Target, string Method, object[] Arguments);
}
