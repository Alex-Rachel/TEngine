using System;

namespace LightProto.Parser
{
    [ProtoContract]
    [ProtoSurrogateFor(typeof(Version))]
    public partial struct VersionProtoParser
    {
        [ProtoMember(1, DataFormat = DataFormat.ZigZag)]
        internal int Major { get; set; }

        [ProtoMember(2, DataFormat = DataFormat.ZigZag)]
        internal int Minor { get; set; }

        [ProtoMember(3, DataFormat = DataFormat.ZigZag)]
        internal int Build { get; set; }

        [ProtoMember(4, DataFormat = DataFormat.ZigZag)]
        internal int Revision { get; set; }

        public static implicit operator Version(VersionProtoParser protoParser)
        {
            if (protoParser.Revision != -1)
            {
                return new Version(
                    protoParser.Major,
                    protoParser.Minor,
                    protoParser.Build,
                    protoParser.Revision
                );
            }

            if (protoParser.Build != -1)
            {
                return new Version(protoParser.Major, protoParser.Minor, protoParser.Build);
            }

            return new Version(protoParser.Major, protoParser.Minor);
        }

        public static implicit operator VersionProtoParser(Version value)
        {
            return new VersionProtoParser
            {
                Major = value.Major,
                Minor = value.Minor,
                Build = value.Build,
                Revision = value.Revision,
            };
        }
    }
}
