using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.UnitTests.Support
{
    /// <summary>
    /// <see cref="IServer"/> that only reports listening addresses, which is all the launcher reads.
    /// </summary>
    internal sealed class FakeServer : IServer
    {
        #region *** Constructors ***
        /// <summary>
        /// Initializes the server with its listening addresses.
        /// </summary>
        /// <param name="addresses">The addresses, for example <c>http://+:9958</c>.</param>
        public FakeServer(params string[] addresses)
        {
            var addressesFeature = new ServerAddressesFeature();

            foreach (var address in addresses)
            {
                addressesFeature.Addresses.Add(address);
            }

            Features.Set<IServerAddressesFeature>(addressesFeature);
        }
        #endregion

        #region *** Properties   ***
        /// <inheritdoc />
        public IFeatureCollection Features { get; } = new FeatureCollection();
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release.
        }

        /// <inheritdoc />
        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull => throw new NotSupportedException();

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        #endregion
    }
}
