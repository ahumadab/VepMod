using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace VepMod.VepFramework
{
    /// <summary>
    ///     Formats an interpolated log message with the invariant culture, so a log reads <c>3.5</c> whatever the
    ///     Windows language: a French one writes <c>(3,5, 0,0)</c>, which tooling cannot split into numbers.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct InvariantLogMessage
    {
        private readonly StringBuilder _builder;

        public InvariantLogMessage(int literalLength, int formattedCount)
        {
            _builder = new StringBuilder(literalLength + formattedCount * 8);
        }

        public void AppendLiteral(string value)
        {
            _builder.Append(value);
        }

        public void AppendFormatted<T>(T value)
        {
            AppendFormatted(value, null);
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            _builder.Append(value is IFormattable formattable
                ? formattable.ToString(format, CultureInfo.InvariantCulture)
                : value?.ToString());
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}

#if !NET6_0_OR_GREATER
// netstandard2.1 lacks the attribute the compiler looks for; it matches it by name.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal sealed class InterpolatedStringHandlerAttribute : Attribute
    {
    }
}
#endif
