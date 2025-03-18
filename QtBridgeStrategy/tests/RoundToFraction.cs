using System;
using Xunit;

public class RoundToFractionTests
{
    [Theory]
    [InlineData(5.75, 4, 6.0)]
    [InlineData(2.33, 2, 2.5)]
    [InlineData(3.14159, 10, 3.1)]
    [InlineData(10.49, 5, 10.4)]
    [InlineData(7.9, 1, 8.0)]
    [InlineData(-2.75, 4, -2.75)]
    [InlineData(0.0, 2, 0.0)]
    [InlineData(5.5, 0.5, 5.5)]
    [InlineData(7.3, 0.2, 7.4)]
    public void RoundToFraction_ShouldReturnExpected(double number, double denominator, double expected)
    {
        double result = RoundToFraction(number, denominator);
        Assert.Equal(expected, result, precision: 10); // Allow floating-point precision
    }

    private static double RoundToFraction(double number, double denominator = 1)
    {
        return Math.Round(number * denominator) / denominator;
    }
}