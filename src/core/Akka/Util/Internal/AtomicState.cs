﻿//-----------------------------------------------------------------------
// <copyright file="AtomicState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Akka.Util.Internal
{
    /// <summary>
    /// Internal state abstraction
    /// </summary>
    internal abstract class AtomicState : AtomicCounterLong, IAtomicState
    {
        private readonly ConcurrentQueue<Action> _listeners;
        private readonly TimeSpan _callTimeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="callTimeout">TBD</param>
        /// <param name="startingCount">TBD</param>
        protected AtomicState(TimeSpan callTimeout, long startingCount)
            : base(startingCount)
        {
            _listeners = new ConcurrentQueue<Action>();
            _callTimeout = callTimeout;
        }

        /// <summary>
        /// Add a listener function which is invoked on state entry
        /// </summary>
        /// <param name="listener">listener implementation</param>
        public void AddListener(Action listener)
        {
            _listeners.Enqueue(listener);
        }

        /// <summary>
        /// Test for whether listeners exist
        /// </summary>
        public bool HasListeners
        {
            get { return !_listeners.IsEmpty; }
        }

        /// <summary>
        /// Notifies the listeners of the transition event via a 
        /// </summary>
        /// <returns>TBD</returns>
        protected async Task NotifyTransitionListeners()
        {
            if (!HasListeners) return;
            await Task
                .Factory
                .StartNew
                (
                    () =>
                    {
                        foreach (var listener in _listeners)
                        {
                            listener.Invoke();
                        }
                    }
                ).ConfigureAwait(false);
        }

        /// <summary>
        /// Shared implementation of call across all states.  Thrown exception or execution of the call beyond the allowed
        /// call timeout is counted as a failed call, otherwise a successful call
        /// </summary>
        /// <param name="task"><see cref="Task"/> Implementation of the call</param>
        /// <returns><see cref="Task"/> containing the result of the call</returns>
        public async Task<T> CallThrough<T>(Func<Task<T>> task)
        {
            var result = default(T);
            try
            {
                result = await task().WaitAsync(_callTimeout).ConfigureAwait(false);
                CallSucceeds();
            }
            catch (Exception ex)
            {
                var capturedException = ExceptionDispatchInfo.Capture(ex);
                CallFails(capturedException.SourceException);
                capturedException.Throw();
            }

            return result;
        }

        public async Task<T> CallThrough<T, TState>(TState state, Func<TState, Task<T>> task)
        {
            var result = default(T);
            try
            {
                result = await task(state).WaitAsync(_callTimeout).ConfigureAwait(false);
                CallSucceeds();
            }
            catch (Exception ex)
            {
                var capturedException = ExceptionDispatchInfo.Capture(ex);
                CallFails(capturedException.SourceException);
                capturedException.Throw();
            }

            return result;
        }

        /// <summary>
        /// Shared implementation of call across all states.  Thrown exception or execution of the call beyond the allowed
        /// call timeout is counted as a failed call, otherwise a successful call
        /// </summary>
        /// <param name="task"><see cref="Task"/> Implementation of the call</param>
        /// <returns><see cref="Task"/> containing the result of the call</returns>
        public async Task CallThrough(Func<Task> task)
        {
            try
            {
                await task().WaitAsync(_callTimeout).ConfigureAwait(false);
                CallSucceeds();
            }
            catch (Exception ex)
            {
                var capturedException = ExceptionDispatchInfo.Capture(ex);
                CallFails(capturedException.SourceException);
                capturedException.Throw();
            }
        }

        public async Task CallThrough<TState>(TState state, Func<TState, Task> task)
        {
            try
            {
                await task(state).WaitAsync(_callTimeout).ConfigureAwait(false);
                CallSucceeds();
            }
            catch (Exception ex)
            {
                var capturedException = ExceptionDispatchInfo.Capture(ex);
                CallFails(capturedException.SourceException);
                capturedException.Throw();
            }
        }

        /// <summary>
        /// Abstract entry point for all states
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public abstract Task<T> Invoke<T>(Func<Task<T>> body);

        public abstract Task<T> InvokeState<T, TState>(TState state, Func<TState, Task<T>> body);

        /// <summary>
        /// Abstract entry point for all states
        /// </summary>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public abstract Task Invoke(Func<Task> body);

        public abstract Task InvokeState<TState>(TState state, Func<TState, Task> body);

        /// <summary>
        /// Invoked when call fails
        /// </summary>
        protected internal abstract void CallFails(Exception cause);

        /// <summary>
        /// Invoked when call succeeds
        /// </summary>
        protected internal abstract void CallSucceeds();

        /// <summary>
        /// Invoked on the transitioned-to state during transition. Notifies listeners after invoking subclass template method _enter
        /// </summary>
        protected abstract void EnterInternal();

        /// <summary>
        /// Enter the state. NotifyTransitionListeners is not awaited -- its "fire and forget". 
        /// It is up to the user to handle any errors that occur in this state.
        /// </summary>
        public void Enter()
        {
            EnterInternal();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            NotifyTransitionListeners();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }

    /// <summary>
    /// This interface represents the parts of the internal circuit breaker state; the behavior stack, watched by, watching and termination queue
    /// </summary>
    public interface IAtomicState
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="listener">TBD</param>
        void AddListener(Action listener);
        /// <summary>
        /// TBD
        /// </summary>
        bool HasListeners { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="body">TBD</param>
        /// <returns>TBD</returns>
        Task<T> Invoke<T>(Func<Task<T>> body);
        /// <summary>
        /// TBD
        /// </summary>
        void Enter();
    }
}
