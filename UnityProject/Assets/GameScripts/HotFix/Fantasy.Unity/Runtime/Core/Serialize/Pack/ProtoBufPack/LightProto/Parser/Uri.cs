using System;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace LightProto.Parser
{
    public sealed class UriProtoParser : IProtoParser<Uri?>
    {
        public static IProtoReader<Uri?> ProtoReader { get; } = new UriProtoReader();
        public static IProtoWriter<Uri?> ProtoWriter { get; } = new UriProtoWriter();

        sealed class UriProtoReader : IProtoReader<Uri?>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public Uri? ParseFrom(ref ReaderContext input)
            {
                var uriString = input.ReadString();
                if (string.IsNullOrEmpty(uriString))
                {
                    return null;
                }

                if (Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out var uri))
                {
                    return uri;
                }

                return null;
            }
        }

        sealed class UriProtoWriter : IProtoWriter<Uri?>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(Uri? value)
            {
                return CodedOutputStream.ComputeStringSize(value?.OriginalString ?? string.Empty);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, Uri? value)
            {
                output.WriteString(value?.OriginalString ?? string.Empty);
            }
        }
    }
}
