using System;

namespace LightProto
{
    public enum DataFormat
    {
        Default,

        /// <summary>
        /// sint32/sint64
        /// </summary>
        ZigZag,

        /// <summary>
        /// fixed32/fixed64/sfixed32/sfixed64
        /// </summary>
        FixedSize,

        /// <summary>
        /// When applied to members of types such as DateTime or TimeSpan, specifies
        /// that the "well known" standardized representation should be use; DateTime uses Timestamp,
        /// TimeSpan uses Duration.
        /// </summary>
        [Obsolete(
            "This option is replaced with "
            + nameof(CompatibilityLevel)
            + ", and is only used for "
            + nameof(CompatibilityLevel.Level200)
            + ", where it changes this field to "
            + nameof(CompatibilityLevel.Level240),
            false
        )]
        WellKnown,
    }
}
