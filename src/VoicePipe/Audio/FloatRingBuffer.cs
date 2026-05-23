namespace VoicePipe.Audio;

internal sealed class FloatRingBuffer
{
    private readonly object _gate = new();
    private float[] _buffer;
    private int _readIndex;
    private int _writeIndex;
    private int _available;

    public FloatRingBuffer(int capacity)
    {
        _buffer = new float[Math.Max(1, capacity)];
    }

    public int Available
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_buffer);
            _readIndex = 0;
            _writeIndex = 0;
            _available = 0;
        }
    }

    public void Write(float[] source, int offset, int count)
    {
        lock (_gate)
        {
            EnsureCapacity(count);

            for (var index = 0; index < count; index++)
            {
                _buffer[_writeIndex] = source[offset + index];
                _writeIndex = (_writeIndex + 1) % _buffer.Length;

                if (_available == _buffer.Length)
                {
                    _readIndex = (_readIndex + 1) % _buffer.Length;
                }
                else
                {
                    _available++;
                }
            }
        }
    }

    public int Read(float[] destination, int offset, int count)
    {
        lock (_gate)
        {
            var read = Math.Min(count, _available);
            for (var index = 0; index < read; index++)
            {
                destination[offset + index] = _buffer[_readIndex];
                _readIndex = (_readIndex + 1) % _buffer.Length;
            }

            _available -= read;
            return read;
        }
    }

    public void TrimTo(int maxAvailable)
    {
        lock (_gate)
        {
            maxAvailable = Math.Max(0, maxAvailable);
            if (_available <= maxAvailable)
            {
                return;
            }

            var drop = _available - maxAvailable;
            _readIndex = (_readIndex + drop) % _buffer.Length;
            _available = maxAvailable;
        }
    }

    private void EnsureCapacity(int incomingCount)
    {
        if (incomingCount <= _buffer.Length)
        {
            return;
        }

        var newCapacity = _buffer.Length;
        while (newCapacity < incomingCount)
        {
            newCapacity *= 2;
        }

        var existing = new float[_available];
        for (var index = 0; index < existing.Length; index++)
        {
            existing[index] = _buffer[(_readIndex + index) % _buffer.Length];
        }

        _buffer = new float[newCapacity];
        Array.Copy(existing, _buffer, existing.Length);
        _readIndex = 0;
        _writeIndex = existing.Length;
    }
}
