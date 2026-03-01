using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using Fantasy;
using MemoryPack;
#pragma warning disable CS9074 // The 'scoped' modifier of parameter doesn't match overridden or implemented member.
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

public sealed class IPAddressFormatter : MemoryPackFormatter<IPAddress>
{
#if FANTASY_UNITY
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref IPAddress? value)
#else
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref IPAddress? value)
#endif
    {
        if (value == null)
        {
            writer.WriteUnmanaged((byte)0); // null flag
            return;
        }

        writer.WriteUnmanaged((byte)1); // not null

        byte[] bytes = value.GetAddressBytes();
        writer.WriteUnmanagedSpan(bytes.AsSpan());
    }

#if FANTASY_UNITY
    public override void Deserialize(ref MemoryPackReader reader, ref IPAddress? value)
#else
    public override void Deserialize(ref MemoryPackReader reader, scoped ref IPAddress? value)
#endif
    {
        byte nullFlag = reader.ReadUnmanaged<byte>();
        if (nullFlag == 0)
        {
            value = null;
            return;
        }

        if (!reader.TryReadCollectionHeader(out int length))
            throw new MemoryPackSerializationException("Invalid IP header");

        if (length != 4 && length != 16)
            throw new MemoryPackSerializationException($"Invalid IP length: {length}");

        ref byte spanRef = ref reader.GetSpanReference(length);
        ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref spanRef, length);

        value = new IPAddress(span);
        reader.Advance(length);
    }
}