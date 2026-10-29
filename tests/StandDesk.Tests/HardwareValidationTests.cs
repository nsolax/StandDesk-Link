using StandDesk.Core;
namespace StandDesk.Tests;
public sealed class HardwareValidationTests
{
    [Theory]
    [InlineData(1162, 1161, 1362, true, false)]
    [InlineData(1162, 1200, 1362, true, false)]
    [InlineData(1162, 1360, 1362, false, true)]
    [InlineData(1162, 1360, 1362, true, false)]
    public void ValidationRejectsFaultsAndWrongDirection(int before, int after, int target, bool fault, bool expected) =>
        Assert.Equal(expected, HardwareValidation.SuccessfulArrival(before, after, target, 0, fault));
}
