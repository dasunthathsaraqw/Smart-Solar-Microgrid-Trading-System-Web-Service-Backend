using System;
using Xunit;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Tests;

public class ScheduleValidatorTests
{
    private static DateTime CreateUtcFromLocal(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    [Fact]
    public void ValidateSlot_ValidWithinDaytimeSchedule_DoesNotThrow()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 28, 10, 0); // Monday
        var utcEnd = CreateUtcFromLocal(2026, 9, 28, 11, 0);
        
        ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "06:00-20:00 Mon-Sun");
    }

    [Fact]
    public void ValidateSlot_ExactlyOnBoundaries_DoesNotThrow()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 28, 6, 0); 
        var utcEnd = CreateUtcFromLocal(2026, 9, 28, 20, 0);
        
        ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "06:00-20:00 Mon-Sun");
    }

    [Fact]
    public void ValidateSlot_InvalidBeforeOpening_Throws()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 28, 5, 0); 
        var utcEnd = CreateUtcFromLocal(2026, 9, 28, 6, 0);
        
        Assert.Throws<InvalidOperationException>(() => 
            ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "06:00-20:00 Mon-Sun"));
    }

    [Fact]
    public void ValidateSlot_InvalidStraddlingOpening_Throws()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 28, 5, 30); 
        var utcEnd = CreateUtcFromLocal(2026, 9, 28, 6, 30);
        
        Assert.Throws<InvalidOperationException>(() => 
            ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "06:00-20:00 Mon-Sun"));
    }

    [Fact]
    public void ValidateSlot_InvalidAfterClosing_Throws()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 28, 22, 0); 
        var utcEnd = CreateUtcFromLocal(2026, 9, 28, 23, 0);
        
        Assert.Throws<InvalidOperationException>(() => 
            ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "06:00-20:00 Mon-Sun"));
    }

    [Fact]
    public void ValidateSlot_DisallowedDay_Throws()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 27, 10, 0); // Sunday
        var utcEnd = CreateUtcFromLocal(2026, 9, 27, 11, 0);
        
        Assert.Throws<InvalidOperationException>(() => 
            ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "06:00-20:00 Mon-Fri"));
    }

    [Fact]
    public void ValidateSlot_OvernightSchedule_Valid()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 28, 22, 0);
        var utcEnd = CreateUtcFromLocal(2026, 9, 28, 23, 0);
        
        ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "20:00-06:00 Mon-Sun");
    }

    [Fact]
    public void ValidateSlot_OvernightSchedule_Invalid()
    {
        var utcStart = CreateUtcFromLocal(2026, 9, 28, 10, 0);
        var utcEnd = CreateUtcFromLocal(2026, 9, 28, 11, 0);
        
        Assert.Throws<InvalidOperationException>(() => 
            ScheduleValidator.ValidateSlotAgainstSchedule(utcStart, utcEnd, "20:00-06:00 Mon-Sun"));
    }
}
