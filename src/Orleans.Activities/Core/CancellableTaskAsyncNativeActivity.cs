﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Activities;
using System.Threading;
using Orleans.Activities.Extensions;
using Orleans.Activities.Hosting;

namespace Orleans.Activities
{
    // On the "access activity context after any await statement" problem, see http://stackoverflow.com/questions/26054585/asynctaskcodeactivity-and-lost-context-after-await/26061482#26061482
    // Async native activity is based on: http://blogs.msdn.com/b/tilovell/archive/2011/06/09/wf4-they-have-asynccodeactivity-why-not-asyncnativeactivity.aspx

    /// <summary>
    /// Base internal class for cancellable Task based async native activities without result.
    /// </summary>
    public abstract class CancellableTaskAsyncNativeActivityBase : NativeActivity
    {
        protected Variable<NoPersistHandle> taskCompletionNoPersistHandle;
        protected Variable<CancellationTokenSource> cancellationTokenSource;

        protected CancellableTaskAsyncNativeActivityBase()
        {
            this.taskCompletionNoPersistHandle = new Variable<NoPersistHandle>();
            this.cancellationTokenSource = new Variable<CancellationTokenSource>();
        }

        protected sealed override bool CanInduceIdle => true;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            metadata.AddImplementationVariable(this.taskCompletionNoPersistHandle);
            metadata.AddImplementationVariable(this.cancellationTokenSource);
            base.CacheMetadata(metadata);
        }

        protected sealed override void Cancel(NativeActivityContext context) => this.cancellationTokenSource.Get(context).Cancel();

        protected void Execute<TTask>(NativeActivityContext context, CancellationTokenSource cts, TTask resultTask,
                Action<NativeActivityContext> postExecute)
            where TTask : Task
        {
            if (resultTask.IsCompleted)
                postExecute(context);
            else
            {
                var activityContext = context.GetActivityContext();

                this.taskCompletionNoPersistHandle.Get(context).Enter(context);

                this.cancellationTokenSource.Set(context, cts);

                var bookmark = context.CreateBookmark(this.BookmarkResumptionCallback);
                // continuations don't use cts, the main task is already completed, we can't cancel it 
                resultTask
                    .ContinueWith((_resultTask) => activityContext.ResumeBookmarkThroughHostAsync(bookmark, postExecute, TimeSpan.MaxValue),
                        TaskContinuationOptions.ExecuteSynchronously).Unwrap()
                    // TODO how to handle exception properly?
                    // AsyncCodeActivity has an AsyncOperationContext and it can call Abort on that context directly, but we are left alone now,
                    // we must schedule an Abort through the host, because NativeActivityContext is already disposed now
                    .ContinueWith((_resumeBookmarkTask) => activityContext.AbortThroughHostAsync(_resumeBookmarkTask.Exception.InnerException),
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously).Unwrap()
                    .ContinueWith((_abortWorkflowInstanceTask) => { var ignored = _abortWorkflowInstanceTask.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        protected void BookmarkResumptionCallback(NativeActivityContext context, Bookmark bookmark, object value)
        {
            var postExecute = value as Action<NativeActivityContext>;

            this.taskCompletionNoPersistHandle.Get(context).Exit(context);

            try
            {
                postExecute(context);
            }
            catch (OperationCanceledException) when (context.IsCancellationRequested)
            {
                // context.MarkCanceled() is not enough, this is a NativeActivity, there can be child activities and other bookmarks
                base.Cancel(context);
            }
        }
    }

    /// <summary>
    /// Base internal class for cancellable Task based async native activities with result.
    /// </summary>
    /// <typeparam name="TActivityResult"></typeparam>
    public abstract class CancellableTaskAsyncNativeActivityWithResultBase<TActivityResult> : NativeActivity<TActivityResult>
    {
        protected Variable<NoPersistHandle> taskCompletionNoPersistHandle;
        protected Variable<CancellationTokenSource> cancellationTokenSource;

        protected CancellableTaskAsyncNativeActivityWithResultBase()
        {
            this.taskCompletionNoPersistHandle = new Variable<NoPersistHandle>();
            this.cancellationTokenSource = new Variable<CancellationTokenSource>();
        }

        protected sealed override bool CanInduceIdle => true;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            metadata.AddImplementationVariable(this.taskCompletionNoPersistHandle);
            metadata.AddImplementationVariable(this.cancellationTokenSource);
            base.CacheMetadata(metadata);
        }

        protected sealed override void Cancel(NativeActivityContext context) => this.cancellationTokenSource.Get(context).Cancel();

        protected void Execute<TTask>(NativeActivityContext context, CancellationTokenSource cts, TTask resultTask,
                Action<NativeActivityContext> postExecute)
            where TTask : Task
        {
            if (resultTask.IsCompleted)
                postExecute(context);
            else
            {
                var activityContext = context.GetActivityContext();

                this.taskCompletionNoPersistHandle.Get(context).Enter(context);

                this.cancellationTokenSource.Set(context, cts);

                var bookmark = context.CreateBookmark(this.BookmarkResumptionCallback);
                // continuations don't use cts, the main task is already completed, we can't cancel it 
                resultTask
                    .ContinueWith((_resultTask) => activityContext.ResumeBookmarkThroughHostAsync(bookmark, postExecute, TimeSpan.MaxValue),
                        TaskContinuationOptions.ExecuteSynchronously).Unwrap()
                    // TODO how to handle exception properly?
                    // AsyncCodeActivity has an AsyncOperationContext and it can call Abort on that context directly, but we are left alone now,
                    // we must schedule an Abort through the host, because NativeActivityContext is already disposed now
                    .ContinueWith((_resumeBookmarkTask) => activityContext.AbortThroughHostAsync(_resumeBookmarkTask.Exception.InnerException),
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously).Unwrap()
                    .ContinueWith((_abortWorkflowInstanceTask) => { var ignored = _abortWorkflowInstanceTask.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        protected void BookmarkResumptionCallback(NativeActivityContext context, Bookmark bookmark, object value)
        {
            var postExecute = value as Action<NativeActivityContext>;

            this.taskCompletionNoPersistHandle.Get(context).Exit(context);

            try
            {
                postExecute(context);
            }
            catch (OperationCanceledException) when (context.IsCancellationRequested)
            {
                // context.MarkCanceled() is not enough, this is a NativeActivity, there can be child activities and other bookmarks
                base.Cancel(context);
            }
        }
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities without result.
    /// </summary>
    public abstract class CancellableTaskAsyncNativeActivity : CancellableTaskAsyncNativeActivityBase
    {
        protected sealed override void Execute(NativeActivityContext context)
        {
            var cts = new CancellationTokenSource();
            var resultTask = ExecuteAsync(context, cts.Token);
            Execute(context, cts, resultTask, _context =>
            {
                resultTask.GetAwaiter().GetResult();
                PostExecute(_context);
            });
        }

        /// <summary>
        /// Implement any Task based async operation.
        /// <para>DO NOT USE context after any await statement! See <see cref="PostExecute"/> to access context after Task completion!
        /// Or use one variant of the <see cref="CancellableContextSafeTaskAsyncNativeActivity{TState}"/> class.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task ExecuteAsync(NativeActivityContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Executed after the Task of <see cref="ExecuteAsync"/> is completed.
        /// <para>In case of any need to access context after Task completed, use this method.</para>
        /// </summary>
        /// <param name="context"></param>
        protected virtual void PostExecute(NativeActivityContext context)
        { }
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities without result, where the Task has result.
    /// </summary>
    /// <typeparam name="TTaskResult"></typeparam>
    public abstract class CancellableTaskAsyncNativeActivity<TTaskResult> : CancellableTaskAsyncNativeActivityBase
    {
        protected sealed override void Execute(NativeActivityContext context)
        {
            var cts = new CancellationTokenSource();
            var resultTask = ExecuteAsync(context, cts.Token);
            Execute(context, cts, resultTask, _context =>
                PostExecute(_context, resultTask.GetAwaiter().GetResult()));
        }

        /// <summary>
        /// Implement any Task based async operation.
        /// <para>DO NOT USE context after any await statement! See <see cref="PostExecute"/> to access context after Task completion!
        /// Or use one variant of the <see cref="CancellableContextSafeTaskAsyncNativeActivity{TState}"/> class.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task<TTaskResult> ExecuteAsync(NativeActivityContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Executed after the Task of <see cref="ExecuteAsync"/> is completed.
        /// <para>To handle the result of the Task (or in case of any need to access context after Task completed), implement this method.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="taskResult"></param>
        /// <returns></returns>
        protected abstract void PostExecute(NativeActivityContext context, TTaskResult taskResult);
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities with result.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public abstract class CancellableTaskAsyncNativeActivityWithResult<TResult> :
        CancellableTaskAsyncNativeActivityWithResult<TResult, TResult>
    {
        /// <summary>
        /// Executed after the Task of <see cref="CancellableTaskAsyncNativeActivityWithResult{TActivityResult, TTaskResult}.ExecuteAsync"/> is completed.
        /// <para>In case of any need to access context after Task completed, implement this method.
        /// The return value of the method is copied to the Result OutArgument of the Activity.
        /// By default the result of the Task is the return value of this method.
        /// </para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="taskResult"></param>
        /// <returns></returns>
        protected override TResult PostExecute(NativeActivityContext context, TResult taskResult) => taskResult;
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities with result, where the Task has different result type than the Activity.
    /// </summary>
    /// <typeparam name="TActivityResult"></typeparam>
    /// <typeparam name="TTaskResult"></typeparam>
    public abstract class CancellableTaskAsyncNativeActivityWithResult<TActivityResult, TTaskResult> :
        CancellableTaskAsyncNativeActivityWithResultBase<TActivityResult>
    {
        protected sealed override void Execute(NativeActivityContext context)
        {
            var cts = new CancellationTokenSource();
            var resultTask = ExecuteAsync(context, cts.Token);
            Execute(context, cts, resultTask, _context =>
                this.Result.Set(_context, PostExecute(_context, resultTask.GetAwaiter().GetResult())));
        }

        /// <summary>
        /// Implement any Task based async operation.
        /// <para>DO NOT USE context after any await statement! See <see cref="PostExecute"/> to access context after Task completion!
        /// Or use one variant of the <see cref="CancellableContextSafeTaskAsyncNativeActivity{TState}"/> class.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task<TTaskResult> ExecuteAsync(NativeActivityContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Executed after the Task of <see cref="ExecuteAsync"/> is completed.
        /// <para>To handle the result of the Task (or in case of any need to access context after Task completed), implement this method.
        /// The return value of the method is copied to the Result OutArgument of the Activity.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="taskResult"></param>
        /// <returns></returns>
        protected abstract TActivityResult PostExecute(NativeActivityContext context, TTaskResult taskResult);
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities without result.
    /// The Task is safely separated from activity context, the Task can't access it even before any await statement.
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public abstract class CancellableContextSafeTaskAsyncNativeActivity<TState> : CancellableTaskAsyncNativeActivityBase
    {
        protected sealed override void Execute(NativeActivityContext context)
        {
            var activityState = PreExecute(context);
            var cts = new CancellationTokenSource();
            var resultTask = ExecuteAsync(activityState, cts.Token);
            Execute(context, cts, resultTask, _context =>
            {
                resultTask.GetAwaiter().GetResult();
                this.PostExecute(_context, activityState);
            });
        }

        /// <summary>
        /// Executed before the Task of <see cref="ExecuteAsync"/> executed.
        /// <para>Create a TState, instead of the activity context, TState will be accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected abstract TState PreExecute(NativeActivityContext context);

        /// <summary>
        /// Implement any Task based async operation.
        /// <para>The activityState is created during <see cref="PreExecute"/> and accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="activityState"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task ExecuteAsync(TState activityState, CancellationToken cancellationToken);

        /// <summary>
        /// Executed after the Task of <see cref="ExecuteAsync"/> is completed.
        /// <para>In case of any need to access context after Task completed, use this method.
        /// The activityState is created during <see cref="PreExecute"/> and accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="activityState"></param>
        protected virtual void PostExecute(NativeActivityContext context, TState activityState)
        { }
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities without result, where the Task has result.
    /// The Task is safely separated from activity context, the Task can't access it even before any await statement.
    /// </summary>
    /// <typeparam name="TTaskResult"></typeparam>
    /// <typeparam name="TState"></typeparam>
    public abstract class CancellableContextSafeTaskAsyncNativeActivity<TTaskResult, TState> : CancellableTaskAsyncNativeActivityBase
    {
        protected sealed override void Execute(NativeActivityContext context)
        {
            var activityState = PreExecute(context);
            var cts = new CancellationTokenSource();
            var resultTask = ExecuteAsync(activityState, cts.Token);
            Execute(context, cts, resultTask, _context =>
                this.PostExecute(_context, activityState, resultTask.GetAwaiter().GetResult()));
        }

        /// <summary>
        /// Executed before the Task of <see cref="ExecuteAsync"/> executed.
        /// <para>Create a TState, instead of the activity context, TState will be accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected abstract TState PreExecute(NativeActivityContext context);

        /// <summary>
        /// Implement any Task based async operation.
        /// <para>The activityState is created during <see cref="PreExecute"/> and accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="activityState"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task<TTaskResult> ExecuteAsync(TState activityState, CancellationToken cancellationToken);

        /// <summary>
        /// Executed after the Task of <see cref="ExecuteAsync"/> is completed.
        /// <para>To handle the result of the Task (or in case of any need to access context after Task completed), implement this method.
        /// The activityState is created during <see cref="PreExecute"/> and accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="activityState"></param>
        /// <param name="taskResult"></param>
        /// <returns></returns>
        protected abstract void PostExecute(NativeActivityContext context, TState activityState, TTaskResult taskResult);
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities with result.
    /// The Task is safely separated from activity context, the Task can't access it even before any await statement.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <typeparam name="TState"></typeparam>
    public abstract class CancellableContextSafeTaskAsyncNativeActivityWithResult<TResult, TState> :
        CancellableContextSafeTaskAsyncNativeActivityWithResult<TResult, TResult, TState>
    {
        /// <summary>
        /// Executed after the Task of <see cref="CancellableContextSafeTaskAsyncNativeActivityWithResult{TActivityResult, TTaskResult, TState}.ExecuteAsync"/> is completed.
        /// <para>In case of any need to access context after Task completed, implement this method.
        /// The activityState is created during <see cref="CancellableContextSafeTaskAsyncNativeActivityWithResult{TActivityResult, TTaskResult, TState}.PreExecute"/>
        /// and accessible during and after Task execution.
        /// The return value of the method is copied to the Result OutArgument of the Activity.
        /// By default the result of the Task is the return value of this method.
        /// </para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="activityState"></param>
        /// <param name="taskResult"></param>
        /// <returns></returns>
        protected override TResult PostExecute(NativeActivityContext context, TState activityState, TResult taskResult) => taskResult;
    }

    /// <summary>
    /// Base class for cancellable Task based async native activities with result, where the Task has different result type than the Activity.
    /// The Task is safely separated from activity context, the Task can't access it even before any await statement.
    /// </summary>
    /// <typeparam name="TActivityResult"></typeparam>
    /// <typeparam name="TTaskResult"></typeparam>
    /// <typeparam name="TState"></typeparam>
    public abstract class CancellableContextSafeTaskAsyncNativeActivityWithResult<TActivityResult, TTaskResult, TState> : 
        CancellableTaskAsyncNativeActivityWithResultBase<TActivityResult>
    {
        protected sealed override void Execute(NativeActivityContext context)
        {
            var activityState = PreExecute(context);
            var cts = new CancellationTokenSource();
            var resultTask = ExecuteAsync(activityState, cts.Token);
            Execute(context, cts, resultTask, _context =>
                this.Result.Set(_context, PostExecute(_context, activityState, resultTask.GetAwaiter().GetResult())));
        }

        /// <summary>
        /// Executed before the Task of <see cref="ExecuteAsync"/> executed.
        /// <para>Create a TState, instead of the activity context, TState will be accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected abstract TState PreExecute(NativeActivityContext context);

        /// <summary>
        /// Implement any Task based async operation.
        /// <para>The activityState is created during <see cref="PreExecute"/> and accessible during and after Task execution.</para>
        /// </summary>
        /// <param name="activityState"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task<TTaskResult> ExecuteAsync(TState activityState, CancellationToken cancellationToken);

        /// <summary>
        /// Executed after the Task of <see cref="ExecuteAsync"/> is completed.
        /// <para>To handle the result of the Task (or in case of any need to access context after Task completed), implement this method.
        /// The activityState is created during <see cref="PreExecute"/> and accessible during and after Task execution.
        /// The return value of the method is copied to the Result OutArgument of the Activity.</para>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="activityState"></param>
        /// <param name="taskResult"></param>
        /// <returns></returns>
        protected abstract TActivityResult PostExecute(NativeActivityContext context, TState activityState, TTaskResult taskResult);
    }
}
