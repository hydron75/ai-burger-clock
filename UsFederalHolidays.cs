using System.Collections.ObjectModel;

namespace AiBurgerClock;

internal static class UsFederalHolidays
{
    // A policy proxy for the usual Monday-Friday federal workweek, not a claim
    // about private AI companies' staffing or actual demand. Rules are from OPM:
    // https://www.opm.gov/policy-data-oversight/pay-leave/federal-holidays/
    // https://www.opm.gov/frequently-asked-questions/pay-and-leave-faq/pay-administration/what-are-federal-holidays/
    // Excludes regional Inauguration Day, state/company holidays, and one-off
    // executive closures. Holiday rules are deliberately separate from DST rules.
    public static IReadOnlyDictionary<DateOnly, string> GetObservedHolidays(int calendarYear)
    {
        if (calendarYear is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(calendarYear));

        var holidays = new Dictionary<DateOnly, string>();
        void Add(DateOnly date, string name)
        {
            if (date.Year == calendarYear)
                holidays.Add(date, name);
        }

        // A January 1 Saturday is observed on December 31 of the preceding year.
        // Include adjacent nominal years before filtering to the observed year.
        for (int year = Math.Max(1, calendarYear - 1); year <= Math.Min(9999, calendarYear + 1); year++)
        {
            Add(Observed(new DateOnly(year, 1, 1)), "New Year's Day");
            if (year >= 1986)
                Add(NthWeekday(year, 1, DayOfWeek.Monday, 3), "Birthday of Martin Luther King, Jr.");
            Add(NthWeekday(year, 2, DayOfWeek.Monday, 3), "Washington's Birthday");
            Add(LastWeekday(year, 5, DayOfWeek.Monday), "Memorial Day");
            if (year >= 2021)
                Add(Observed(new DateOnly(year, 6, 19)), "Juneteenth National Independence Day");
            Add(Observed(new DateOnly(year, 7, 4)), "Independence Day");
            Add(NthWeekday(year, 9, DayOfWeek.Monday, 1), "Labor Day");
            Add(NthWeekday(year, 10, DayOfWeek.Monday, 2), "Columbus Day");
            Add(Observed(new DateOnly(year, 11, 11)), "Veterans Day");
            Add(NthWeekday(year, 11, DayOfWeek.Thursday, 4), "Thanksgiving Day");
            Add(Observed(new DateOnly(year, 12, 25)), "Christmas Day");
        }

        return new ReadOnlyDictionary<DateOnly, string>(holidays);
    }

    private static DateOnly Observed(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(1),
        _ => date
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int occurrence)
    {
        var first = new DateOnly(year, month, 1);
        int offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (occurrence - 1));
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        int offset = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-offset);
    }
}
