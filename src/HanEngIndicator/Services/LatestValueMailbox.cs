namespace HanEngIndicator.Services;

/// <summary>Bounds UI work to one queued callback, replacing stale values.</summary>
public sealed class LatestValueMailbox<T>
{
    private readonly object _gate = new();
    private T _value = default!;
    private bool _pending;

    public bool Publish(T value)
    {
        lock (_gate)
        {
            _value = value;
            if (_pending) return false;
            _pending = true;
            return true;
        }
    }

    public T Take()
    {
        lock (_gate)
        {
            _pending = false;
            return _value;
        }
    }

    public void Cancel()
    {
        lock (_gate) _pending = false;
    }
}
