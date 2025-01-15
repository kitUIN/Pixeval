#region Copyright (c) Pixeval/Pixeval.Caching
// GPL v3 License
// 
// Pixeval/Pixeval.Caching
// Copyright (c) 2025 Pixeval.Caching/CacheTable.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixeval.Caching;

public class CacheTable<TKey, THeader, TProtocol>(
    TProtocol protocol,
    CacheToken token)
    where THeader : unmanaged
    where TKey : IEquatable<TKey>
    where TProtocol : ICacheProtocol<TKey, THeader>
{
    public MemoryMappedFileMemoryManager MemoryManager { get; } = new(token);

    private Dictionary<TKey, (nint ptr, int allocatedLength)> _cacheTable = [];

    private PriorityQueue<TKey, int> _lruCacheIndex = new(Comparer<int>.Create((x, y) => y.CompareTo(x)));

    private readonly ManualResetEvent _readEvent = new(true);
    private readonly ManualResetEvent _writeEvent = new(true);
    private readonly ManualResetEvent _collectEvent = new(true);


    // ReSharper disable once InconsistentNaming
    public int CacheLRUFactor { get; set; } = 2;

    private TProtocol _protocol = protocol;

    /// <summary>
    /// While calling this method, all cache operations should be halted.
    /// </summary>
    private unsafe void PurgeCompact()
    {
        WaitHandle.WaitAny([_readEvent, _writeEvent]);
        _collectEvent.Reset();
        var retain = _lruCacheIndex.Count / CacheLRUFactor;
        var newPriorityQueue = new PriorityQueue<TKey, int>();
        for (var i = 0; i < retain; i++)
        {
            _lruCacheIndex.TryDequeue(out var element, out var priority);
            newPriorityQueue.Enqueue(element!, priority);
        }

        var garbage = new Dictionary<nint, int>();

        foreach (var key in _lruCacheIndex.UnorderedItems.Select(x => x.Element))
        {
            if (TryReadCache0(key, out var span, true))
            {
                var pointer = (byte*) Unsafe.AsPointer(ref MemoryMarshal.GetReference(span)) - TProtocol.GetHeaderLength();
                garbage[(nint) pointer] = span.Length;
            }
        }

        var grouped = _cacheTable.Values.ToDictionary().GroupBy(
            tuple => MemoryManager.BumpPointerAllocators.First(pair => pair.Value.GetBlock((byte*) tuple.Key) != default).Value,
            tuple => tuple);
        foreach (var group in grouped)
        {
            var replacement = group.Key.Compact(group.ToDictionary(tuple => tuple.Key, tuple => tuple.Value), garbage.Keys.ToHashSet());
            // forward reference
            _cacheTable = _cacheTable.SelectMany(pair =>
            {
                return replacement.TryGetValue(pair.Value.ptr, out var newPointer)
                    ? new[] { KeyValuePair.Create(pair.Key, (newPointer, pair.Value.allocatedLength)) }
                    : new[] { pair };
            }).ToDictionary();
        }

        _lruCacheIndex = newPriorityQueue;
    }

    /// <summary>
    /// Hey don't fucking call this on the UI thread. As to why I didn't make it async: because unsafe, await, and span are mutually exclusive options :)
    /// - Dylech30th 1/13/2025
    /// </summary>
    /// <param name="key"></param>
    /// <param name="span"></param>
    /// <returns></returns>
    public AllocatorState TryCache(TKey key, Span<byte> span)
    {
        return TryCache0(key, span, false);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private unsafe AllocatorState TryCache0(TKey key, Span<byte> span, bool collected)
    {
        _collectEvent.WaitOne();
        _readEvent.Reset();
        if (_cacheTable.ContainsKey(key))
        {
            return AllocatorState.AllocationSuccess;
        }

        var header = _protocol.SerializeHeader(_protocol.GetHeader(key));

        var result = MemoryManager.DominantAllocator.TryAllocate(header.Length + _protocol.GetDataLength(_protocol.GetHeader(key)), out var cacheArea);
        switch (result)
        {
            case AllocatorState.AllocationSuccess:
                header.CopyTo(cacheArea);
                span.CopyTo(cacheArea[header.Length..]);
                _cacheTable[key] = ((nint) Unsafe.AsPointer(ref cacheArea.GetPinnableReference()), cacheArea.Length);

                _lruCacheIndex.Enqueue(key, 0);
                return AllocatorState.AllocationSuccess;
            case AllocatorState.OutOfMemory when collected:
                return result;
            case AllocatorState.OutOfMemory:
                PurgeCompact();
                // ReSharper disable once TailRecursiveCall
                return TryCache0(key, span, true);
            default:
                return result;
        }
    }

    public bool TryReadCache(TKey key, out Span<byte> span)
    {
        return TryReadCache0(key, out span, false);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private unsafe bool TryReadCache0(TKey key, out Span<byte> span, bool transparent)
    {
        _collectEvent.WaitOne();
        _readEvent.Reset();
        if (_cacheTable.TryGetValue(key, out var tuple))
        {
            var headerLength = TProtocol.GetHeaderLength();
            var header = new Span<byte>((byte*) tuple.ptr, headerLength);
            var headerStruct = _protocol.DeserializeHeader(header);
            var dataLength = _protocol.GetDataLength(headerStruct);
            var totalSpan = new Span<byte>((void*) tuple.ptr, tuple.allocatedLength);
            span = totalSpan[headerLength..(headerLength + dataLength)];

            if (!transparent)
            {
                _lruCacheIndex.Remove(key, out _, out var oldPriority);
                _lruCacheIndex.Enqueue(key, oldPriority + 1);
            }

            return true;
        }

        span = Span<byte>.Empty;
        return false;
    }
}
