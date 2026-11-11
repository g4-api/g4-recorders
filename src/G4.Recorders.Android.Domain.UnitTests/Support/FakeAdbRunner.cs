using G4.Recorders.Android.Domain.Adb;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.UnitTests.Support
{
    /// <summary>
    /// Scripted <see cref="IAdbRunner"/>: records every argument list and answers from rules matched by substring.
    /// </summary>
    /// <remarks>
    /// A rule matches when the space-joined arguments contain its key; the last added matching rule wins, so a test
    /// overrides the defaults by adding a more specific rule. Unmatched commands succeed with empty output.
    /// </remarks>
    internal sealed class FakeAdbRunner : IAdbRunner
    {
        #region *** Fields       ***
        // Answers by command fragment, newest last.
        private readonly List<(string Fragment, Func<AdbResult> Answer)> _rules = [];
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets every command run, as space-joined argument lists.
        /// </summary>
        public List<string> Commands { get; } = [];
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Adds a successful text answer for commands that contain a fragment.
        /// </summary>
        /// <param name="fragment">The fragment the joined arguments must contain.</param>
        /// <param name="output">The standard output.</param>
        /// <returns>This runner, for chaining.</returns>
        public FakeAdbRunner Add(string fragment, string output)
        {
            return Add(fragment, output, exitCode: 0);
        }

        /// <summary>
        /// Adds a text answer with an exit code for commands that contain a fragment.
        /// </summary>
        /// <param name="fragment">The fragment the joined arguments must contain.</param>
        /// <param name="output">The standard output.</param>
        /// <param name="exitCode">The exit code; non-zero makes the launcher's checked commands fail.</param>
        /// <returns>This runner, for chaining.</returns>
        public FakeAdbRunner Add(string fragment, string output, int exitCode)
        {
            _rules.Add((fragment, () => new AdbResult(exitCode, Encoding.UTF8.GetBytes(output), string.Empty)));

            return this;
        }

        /// <summary>
        /// Adds a failure for commands that contain a fragment.
        /// </summary>
        /// <param name="fragment">The fragment the joined arguments must contain.</param>
        /// <param name="exception">The exception to throw.</param>
        /// <returns>This runner, for chaining.</returns>
        public FakeAdbRunner AddFailure(string fragment, Exception exception)
        {
            _rules.Add((fragment, () => throw exception));

            return this;
        }

        /// <summary>
        /// Adds the answers of one connected device with a clean accessibility setup and a successful install.
        /// </summary>
        /// <param name="serial">The device serial.</param>
        /// <returns>This runner, for chaining.</returns>
        public FakeAdbRunner AddHealthyDevice(string serial)
        {
            return Add("devices", $"List of devices attached\n{serial}\tdevice\n")
                .Add("install", "Performing Streamed Install\nSuccess")
                .Add("settings get secure", "null")
                .Add("am start", "Starting: Intent { cmp=... }")
                .Add("monkey", "Events injected: 1")
                .Add("pidof", "4242");
        }

        /// <inheritdoc />
        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            // Record the command, then answer from the newest matching rule.
            var command = string.Join(' ', arguments);
            Commands.Add(command);
            var rule = _rules.LastOrDefault(i => command.Contains(i.Fragment, StringComparison.Ordinal));
            var result = rule.Answer is null ? new AdbResult(0, [], string.Empty) : rule.Answer();

            return Task.FromResult(result);
        }
        #endregion
    }
}
