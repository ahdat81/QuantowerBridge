using System;
using System.Text.Json;
using Xunit;

public class CalculateOgStopTests
{
    [Theory]
    [InlineData("{\"ogStop\":105}", false, 100, 105)] // Long, ogStop is higher
    [InlineData("{\"ogStop\":95}", false, 100, 100)] // Long, ogStop is lower (no effect)
    [InlineData("{\"ogStop\":95}", true, 100, 95)]  // Short, ogStop is lower
    [InlineData("{\"ogStop\":105}", true, 100, 100)] // Short, ogStop is higher (no effect)
    [InlineData("{\"ogStop\":100}", false, 100, 100)] // Long, ogStop equals stopPrice
    [InlineData("{\"ogStop\":100}", true, 100, 100)] // Short, ogStop equals stopPrice
    [InlineData("{\"ogStop\":0}", false, 100, 100)] // ogStop is 0 (should not change stopPrice)
    [InlineData("", false, 100, 100)] // Empty JSON
    [InlineData(null, true, 100, 100)] // Null JSON
    public void CalculateOgStop_ShouldReturnExpected(string jsonString, bool isShort, double stopPrice, double expected)
    {
        double result = CalculateOgStop(jsonString, isShort, stopPrice);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateOgStop_MalformedJson_ShouldThrowException()
    {
        string malformedJson = "{invalidJson}";

        Assert.Throws<JsonException>(() => CalculateOgStop(malformedJson, false, 100));
    }

    private static double CalculateOgStop(string jsonString, bool isShort, double stopPrice)
    {
        double stop = stopPrice;
        if (jsonString != null && jsonString != "")
        {
            OrderInfoAdditional additionalInfo = JsonSerializer.Deserialize<OrderInfoAdditional>(jsonString);
            if (additionalInfo.ogStop > 0)
            {
                double ogStop = additionalInfo.ogStop;
                if (!isShort && stopPrice < ogStop)
                    stop = ogStop;
                else if (isShort && stopPrice > ogStop)
                    stop = ogStop;
            }
        }
        return stop;
    }

    private class OrderInfoAdditional
    {
        public double ogStop { get; set; }
    }
}