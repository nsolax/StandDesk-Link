namespace StandDesk.Core;

public static class HardwareValidation
{
    public static bool SuccessfulArrival(int before, int after, int target, short speed, bool hadFault) =>
        !hadFault && speed == 0 && Math.Abs(after - target) <= 30 && Math.Abs(after - before) >= 20
        && Math.Sign(after - before) == Math.Sign(target - before);
}
