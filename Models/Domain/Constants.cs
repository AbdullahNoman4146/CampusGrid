namespace CampusGrid.Models.Domain;

public static class DirectiveType
{
    public const string SolarReduction = "solar_reduction";
    public const string MinimumBatteryReserve = "minimum_battery_reserve";
    public const string NoChargeWindow = "no_charge_window";
    public const string NoDischargeWindow = "no_discharge_window";
    public const string MaxGridWindow = "max_grid_window";
    public const string NoOp = "no_op";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        SolarReduction,
        MinimumBatteryReserve,
        NoChargeWindow,
        NoDischargeWindow,
        MaxGridWindow,
        NoOp
    };
}

public static class BatteryAction
{
    public const string Charge = "charge";
    public const string Discharge = "discharge";
    public const string Idle = "idle";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Charge,
        Discharge,
        Idle
    };
}
