using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace InsoleApi.Database.Converter
{
    public class DateTimeZoneConverter : ValueConverter<DateTime, DateTime>
    {
        public DateTimeZoneConverter() : base(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
    }
}
