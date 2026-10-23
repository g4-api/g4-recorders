using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using G4.Recorders.Uia.Domain.Models;

namespace G4.Recorders.Uia.Domain.Middlewares
{
    /// <summary>
    /// Retains pre-dispatch UI Automation targets for keyboard transitions until
    /// each matching physical key is released.
    /// </summary>
    /// <remarks>
    /// Key actions can close a dialog or transfer focus before Key Up is processed.
    /// Retaining the first Key Down state keeps both transitions bound to one target.
    /// </remarks>
    internal sealed class KeyboardTargetResolver
    {
        #region *** Fields       ***
        private readonly Dictionary<KeyboardKeyIdentity, KeyboardPressState> _pressedTargets = [];
        private readonly IUiaRecorderRepository _repository;
        private readonly object _syncRoot = new();
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="KeyboardTargetResolver"/> class.
        /// </summary>
        /// <param name="repository">The repository used only when a release has no retained press.</param>
        internal KeyboardTargetResolver(IUiaRecorderRepository repository)
        {
            // Require the focused-element provider before accepting keyboard state.
            ArgumentNullException.ThrowIfNull(
                argument: repository,
                paramName: nameof(repository));

            // Retain the provider only for releases captured after their physical press.
            _repository = repository;
        }
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Removes every retained keyboard press so state cannot cross capture lifecycles.
        /// </summary>
        internal void Clear()
        {
            // Synchronize lifecycle cleanup with any focused-element lookup in progress.
            lock (_syncRoot)
            {
                _pressedTargets.Clear();
            }
        }

        /// <summary>
        /// Resolves the first target for a pressed physical key and reuses it for auto-repeat.
        /// </summary>
        /// <param name="identity">The stable physical-key identity shared by Down and Up.</param>
        /// <param name="keyText">The display text resolved for the first press.</param>
        /// <param name="modifiers">The shortcut modifiers captured with the first press; empty for typed text.</param>
        /// <param name="capturedChain">The pre-press focus snapshot captured before this key's own
        /// effect changed focus.</param>
        /// <param name="isFocusMoving">A value indicating whether the key can move focus. A missing snapshot
        /// for such a key remains unresolved because a later focused lookup can observe the element that
        /// received the key's effect instead of its input. A key that cannot move focus falls back to the
        /// element focused when the press is processed, which is still the element that received it.</param>
        /// <returns>The retained keyboard target and its resolution source.</returns>
        internal KeyboardTargetResolution ResolveDown(
            KeyboardKeyIdentity identity,
            string keyText,
            string[] modifiers,
            UiaChainModel capturedChain,
            bool isFocusMoving)
        {
            // Serialize lookup and publication so lifecycle cleanup cannot retain a partially resolved press.
            lock (_syncRoot)
            {
                // Preserve the first physical press target across all auto-repeat Down messages.
                if (_pressedTargets.TryGetValue(identity, out var retainedState))
                {
                    return new KeyboardTargetResolution(
                        retainedState.Chain,
                        retainedState.KeyText,
                        retainedState.Modifiers,
                        KeyboardTargetSource.RepeatedPress);
                }

                // Prefer the pre-dispatch focus snapshot so navigation cannot bind the press to its destination.
                var hasCapturedChain = capturedChain?.Path?.Count > 0;
                var chain = hasCapturedChain
                    ? capturedChain
                    : null;
                var source = hasCapturedChain
                    ? KeyboardTargetSource.CapturedSnapshot
                    : KeyboardTargetSource.MissingSnapshot;

                // Recover typed text whose snapshot was invalidated by a focus notification (for example an
                // autocomplete list re-raising focus on the same edit box); the recipient has not changed.
                if (!hasCapturedChain && !isFocusMoving)
                {
                    var focusedChain = GetFocusedChain(_repository);
                    var hasFocusedChain = focusedChain?.Path?.Count > 0;
                    chain = hasFocusedChain ? focusedChain : null;
                    source = hasFocusedChain ? KeyboardTargetSource.FocusFallback : source;
                }

                var pressState = new KeyboardPressState
                {
                    Chain = chain,
                    KeyText = keyText,
                    Modifiers = modifiers ?? []
                };

                // Publish the complete state only after target, text, and modifiers have been materialized.
                _pressedTargets.Add(identity, pressState);

                return new KeyboardTargetResolution(chain, keyText, pressState.Modifiers, source);
            }
        }

        /// <summary>
        /// Resolves a released physical key from its retained first-press target.
        /// </summary>
        /// <param name="identity">The stable physical-key identity shared by Down and Up.</param>
        /// <returns>The paired state, or a focused-element fallback for an orphaned release.</returns>
        internal KeyboardTargetResolution ResolveUp(KeyboardKeyIdentity identity)
        {
            // Consume a paired press exactly once so later releases cannot reuse obsolete state.
            lock (_syncRoot)
            {
                if (_pressedTargets.Remove(identity, out var retainedState))
                {
                    return new KeyboardTargetResolution(
                        retainedState.Chain,
                        retainedState.KeyText,
                        retainedState.Modifiers,
                        KeyboardTargetSource.PairedRelease);
                }

                // Preserve legacy focused-element behavior when recording begins after Key Down.
                var fallbackChain = _repository.GetElementChain();

                return new KeyboardTargetResolution(
                    fallbackChain,
                    keyText: string.Empty,
                    modifiers: [],
                    KeyboardTargetSource.OrphanFallback);
            }
        }

        // Reads the currently focused chain for a non-navigating press whose snapshot is missing.
        // Provider failures resolve to null so the key is still recorded, without a target, instead of lost.
        private static UiaChainModel GetFocusedChain(IUiaRecorderRepository repository)
        {
            try
            {
                // Resolve on the worker thread; the low-level hook never reaches this lookup.
                return repository.GetElementChain();
            }
            catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
            {
                // Transient provider and elevated-window failures leave the press untargeted.
                return null;
            }
        }
        #endregion

        #region *** Nested Types ***
        private sealed class KeyboardPressState
        {
            internal UiaChainModel Chain { get; init; }

            internal string KeyText { get; init; }

            internal string[] Modifiers { get; init; }
        }
        #endregion
    }

    /// <summary>
    /// Identifies one physical keyboard key independently of display text and layout.
    /// </summary>
    internal readonly record struct KeyboardKeyIdentity
    {
        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="KeyboardKeyIdentity"/> structure.
        /// </summary>
        /// <param name="virtualKey">The Windows virtual-key code.</param>
        /// <param name="scanCode">The hardware scan code reported by the low-level hook.</param>
        /// <param name="extended">A value indicating whether Windows marked the key as extended.</param>
        internal KeyboardKeyIdentity(uint virtualKey, uint scanCode, bool extended)
        {
            Extended = extended;
            ScanCode = scanCode;
            VirtualKey = virtualKey;
        }
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets a value indicating whether Windows marked the key as extended.
        /// </summary>
        internal bool Extended { get; }

        /// <summary>
        /// Gets the hardware scan code reported by the low-level hook.
        /// </summary>
        internal uint ScanCode { get; }

        /// <summary>
        /// Gets the Windows virtual-key code.
        /// </summary>
        internal uint VirtualKey { get; }
        #endregion
    }

    /// <summary>
    /// Identifies how a keyboard transition obtained its UI Automation target.
    /// </summary>
    internal enum KeyboardTargetSource
    {
        CapturedSnapshot,
        FocusFallback,
        MissingSnapshot,
        OrphanFallback,
        PairedRelease,
        RepeatedPress
    }

    /// <summary>
    /// Contains the resolved keyboard target, retained key text, and resolution source.
    /// </summary>
    internal readonly struct KeyboardTargetResolution
    {
        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="KeyboardTargetResolution"/> structure.
        /// </summary>
        /// <param name="chain">The resolved UI Automation target chain.</param>
        /// <param name="keyText">The key text retained from the first press.</param>
        /// <param name="modifiers">The shortcut modifiers retained from the first press.</param>
        /// <param name="source">The source used to resolve the transition.</param>
        internal KeyboardTargetResolution(
            UiaChainModel chain,
            string keyText,
            string[] modifiers,
            KeyboardTargetSource source)
        {
            Chain = chain;
            KeyText = keyText;
            Modifiers = modifiers;
            Source = source;
        }
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the resolved UI Automation target chain.
        /// </summary>
        internal UiaChainModel Chain { get; }

        /// <summary>
        /// Gets the key text retained from the first press, or an empty value for an orphaned release.
        /// </summary>
        internal string KeyText { get; }

        /// <summary>
        /// Gets the shortcut modifiers retained from the first press; empty for typed text and orphaned releases.
        /// </summary>
        internal string[] Modifiers { get; }

        /// <summary>
        /// Gets the source used to resolve the keyboard transition.
        /// </summary>
        internal KeyboardTargetSource Source { get; }
        #endregion
    }
}
