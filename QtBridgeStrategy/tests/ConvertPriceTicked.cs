using System;
using Xunit;

public class ConvertPriceTickedTests
{
    [Theory]
    [InlineData(0.25, 10.37, 10.25)]
    [InlineData(0.25, 10.50, 10.50)]
    [InlineData(0.1, 5.27, 5.3)]
    [InlineData(0.5, 9.81, 10.0)]
    [InlineData(0.01, 7.645, 7.65)]
    [InlineData(0.05, 3.46, 3.45)]
    [InlineData(0.2, -4.12, -4.2)]
    [InlineData(0.01, 0.0, 0.0)]
    [InlineData(1.0, 10.0, 10.0)]
    [InlineData(0.5, 12.1, 12.0)]
    public void ConvertPriceTicked_ShouldReturnExpected(double tickSize, double price, double expected)
    {
        double result = ConvertPriceTicked(tickSize, price);
        Assert.Equal(expected, result, precision: 10); // Allow floating-point precision
    }

    private static double ConvertPriceTicked(double tickSize, double price)
    {
        if (price % tickSize != 0)
            return RoundToFraction(price, 1.0 / tickSize);
        return price;
    }

    private static double RoundToFraction(double number, double denominator = 1)
    {
        return Math.Round(number * denominator) / denominator;
    }
}