using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Threading;

namespace SysDiag.Core;

/// <summary>Todos los objetos del Agente de Windows Update pertenecen a un único apartamento STA.</summary>
[SupportedOSPlatform("windows")]
public sealed class ComWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private bool _disposed;

    public ComWorker(string name = "SysDiag.COM")
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Loop()
    {
        try
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { AppLog.Write($"Fallo en el hilo COM: {ex}", "ERROR"); }
            }
        }
        finally { _queue.Dispose(); } // no disponer mientras el consumidor todavía la usa
    }

    public T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (Thread.CurrentThread == _thread)
        {
            lock (_gate) if (_disposed) throw new ObjectDisposedException(nameof(ComWorker));
            return work(); // una llamada reentrante no debe esperar a sí misma
        }
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ComWorker));
            _queue.Add(() =>
            {
                try { completion.SetResult(work()); }
                catch (Exception ex) { completion.SetException(ex); }
            });
        }
        return completion.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CompleteAdding();
        }
        // COM no se puede abortar de manera segura. El consumidor termina y libera su cola por sí mismo.
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(5));
    }
}
