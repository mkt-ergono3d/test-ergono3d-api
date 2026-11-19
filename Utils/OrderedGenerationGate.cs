using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace InsoleApi.Utils
{
    public sealed class OrderedGenerationGate
    {
        private readonly object _sync = new();
        private readonly Queue<Waiter> _queue = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _pumpRunning;

        // 1. 等待申请锁：按入队顺序等待。
        public Task<LockLease> WaitForLockAsync(
            CancellationToken cancellationToken = default)
        {
            var waiter = new Waiter(cancellationToken);

            lock (_sync)
            {
                _queue.Enqueue(waiter);
                StartPumpIfNeeded_NoLock();
            }

            return waiter.Task;
        }

        // 2. 释放锁：只能释放自己取得的 LockLease。
        public void ReleaseLock(LockLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);

            if (!lease.TryMarkReleased(this))
                return; // 已释放，忽略重复调用。

            _semaphore.Release();

            lock (_sync)
            {
                StartPumpIfNeeded_NoLock();
            }
        }

        private void StartPumpIfNeeded_NoLock()
        {
            if (_pumpRunning)
                return;

            _pumpRunning = true;
            _ = PumpAsync();
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                Waiter? waiter;

                lock (_sync)
                {
                    // 跳过在排队阶段已取消的请求。
                    while (_queue.Count > 0 && _queue.Peek().Task.IsCompleted)
                    {
                        _queue.Dequeue().DisposeCancellationRegistration();
                    }

                    if (_queue.Count == 0)
                    {
                        _pumpRunning = false;
                        return;
                    }

                    waiter = _queue.Dequeue();
                }

                await _semaphore.WaitAsync().ConfigureAwait(false);

                bool granted;

                lock (_sync)
                {
                    granted = waiter.TrySetResult(new LockLease(this));

                    if (granted)
                        _pumpRunning = false;
                }

                waiter.DisposeCancellationRegistration();

                if (granted)
                    return;

                // 请求在取得信号量前被取消，释放后继续处理队首。
                _semaphore.Release();
            }
        }

        public sealed class LockLease : IDisposable
        {
            private OrderedGenerationGate? _owner;

            internal LockLease(OrderedGenerationGate owner)
            {
                _owner = owner;
            }

            internal bool TryMarkReleased(OrderedGenerationGate expectedOwner)
            {
                var owner = Volatile.Read(ref _owner);

                if (owner is null)
                    return false;

                if (!ReferenceEquals(owner, expectedOwner))
                    throw new InvalidOperationException("该锁不属于当前队列实例。");

                return Interlocked.Exchange(ref _owner, null) is not null;
            }

            public void Dispose()
            {
                var owner = Volatile.Read(ref _owner);
                owner?.ReleaseLock(this);
            }
        }

        private sealed class Waiter
        {
            private readonly TaskCompletionSource<LockLease> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly CancellationTokenRegistration _registration;
            private int _registrationDisposed;

            public Waiter(CancellationToken cancellationToken)
            {
                if (cancellationToken.CanBeCanceled)
                {
                    _registration = cancellationToken.Register(
                        static state => ((Waiter)state!)._source.TrySetCanceled(),
                        this);
                }
            }

            public Task<LockLease> Task => _source.Task;

            public bool TrySetResult(LockLease lease) =>
                _source.TrySetResult(lease);

            public void DisposeCancellationRegistration()
            {
                if (Interlocked.Exchange(ref _registrationDisposed, 1) == 0)
                    _registration.Dispose();
            }
        }
    }
}