namespace LightProto
{
    /// <summary>
    /// Defines the compatibility level / conventions to use when encoding common
    /// types into protobuf. When starting a new green-field project the highest
    /// available level can be safely applied, but note that changing the
    /// compatibility level changes the encoding. For this reason, it should not
    /// be casually changed on brown-field projects, unless you are also knowingly
    /// breaking the encoding requirements of pre-existing data. If not specified,
    /// the oldest (lowest number) is assumed, for safety.
    /// </summary>
    public enum CompatibilityLevel
    {
        /// <summary>
        /// Functionally identical to <see cref="Level200"/>
        /// </summary>
        NotSpecified = 0,

        /// <summary>
        /// Uses bcl.proto for <see cref="DateTime"/>, <see cref="TimeSpan"/>, <see cref="Guid"/> and <see cref="decimal"/>, for compatibility
        /// with all versions of protobuf-net, at the expense of being inconvenient for use with other protobuf implementations.
        /// </summary>
        Level200 = 200,

        /// <summary>
        /// Like <see cref="Level200"/>, but uses '.google.protobuf.Timestamp' for <see cref="DateTime"/> and '.google.protobuf.Duration' for <see cref="TimeSpan"/>.
        /// This is functionally identical to a <see cref="Level200"/> configuration that specifies <see cref="DataFormat.WellKnown"/>.
        /// </summary>
        Level240 = 240,

        /// <summary>
        /// Like <see cref="Level240"/>, but uses 'string' for <see cref="Guid"/> (big-endian hyphenated UUID format; a shorter 'bytes' variant is also available via <see cref="DataFormat.FixedSize"/>)
        /// and <see cref="decimal"/> (invariant "general" format).
        /// </summary>
        Level300 = 300,
    }
}
