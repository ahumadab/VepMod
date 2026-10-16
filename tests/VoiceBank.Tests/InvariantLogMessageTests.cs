using System.Globalization;
using VepMod.VepFramework;
using Xunit;

namespace VoiceBank.Tests;

public class InvariantLogMessageTests
{
    private static string Format(ref InvariantLogMessage message)
    {
        return message.ToString();
    }

    [Fact]
    public void Numbers_ignore_a_french_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            var x = 3.5f;
            var y = -0.25;
            var name = "lean";

            Assert.Equal("lean=(3.5, -0.25) 7", Format($"{name}=({x:F1}, {y}) {7}"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
