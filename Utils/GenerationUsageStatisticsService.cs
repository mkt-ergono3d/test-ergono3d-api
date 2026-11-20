using InsoleApi.Database;
using Microsoft.EntityFrameworkCore;

namespace InsoleApi.Utils
{
    public sealed class GenerationUsageStatisticsService
    {
        private readonly PrimaryDbContext _primaryDbContext;

        public GenerationUsageStatisticsService(PrimaryDbContext primaryDbContext)
        {
            _primaryDbContext = primaryDbContext;
        }

        public async Task RecordUserGenerationAsync(CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var dayUtc = DateOnly.FromDateTime(nowUtc);

            await _primaryDbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO generation_usage_daily (day_utc, user_generation_count, failed_check_retry_count, created_at_utc, updated_at_utc)
                VALUES ({dayUtc}, 1, 0, {nowUtc}, {nowUtc})
                ON CONFLICT (day_utc) DO UPDATE SET
                    user_generation_count = generation_usage_daily.user_generation_count + 1,
                    updated_at_utc = EXCLUDED.updated_at_utc;
                """, cancellationToken);
        }

        public async Task RecordFailedCheckRetryAsync(CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var dayUtc = DateOnly.FromDateTime(nowUtc);

            await _primaryDbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO generation_usage_daily (day_utc, user_generation_count, failed_check_retry_count, created_at_utc, updated_at_utc)
                VALUES ({dayUtc}, 0, 1, {nowUtc}, {nowUtc})
                ON CONFLICT (day_utc) DO UPDATE SET
                    failed_check_retry_count = generation_usage_daily.failed_check_retry_count + 1,
                    updated_at_utc = EXCLUDED.updated_at_utc;
                """, cancellationToken);
        }

        public async Task<GenerationUsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
            var weekStartUtc = todayUtc.AddDays(-(((int)todayUtc.DayOfWeek + 6) % 7));
            var monthStartUtc = new DateOnly(todayUtc.Year, todayUtc.Month, 1);
            var trailingSevenDayStartUtc = todayUtc.AddDays(-6);
            var earliestDate = new[] { weekStartUtc, monthStartUtc, trailingSevenDayStartUtc }.Min();

            var dailyCounts = await _primaryDbContext.GenerationUsageDaily
                .AsNoTracking()
                .Where(item => item.DayUtc >= earliestDate && item.DayUtc <= todayUtc)
                .Select(item => new { item.DayUtc, item.UserGenerationCount, item.FailedCheckRetryCount })
                .ToListAsync(cancellationToken);

            long SumGenerations(Func<DateOnly, bool> includeDay) => dailyCounts
                .Where(item => includeDay(item.DayUtc))
                .Sum(item => item.UserGenerationCount);

            long SumFailedCheckRetries(Func<DateOnly, bool> includeDay) => dailyCounts
                .Where(item => includeDay(item.DayUtc))
                .Sum(item => item.FailedCheckRetryCount);

            var trailingSevenDayTotal = SumGenerations(day => day >= trailingSevenDayStartUtc);

            return new GenerationUsageSnapshot
            {
                DayUtc = todayUtc,
                WeekStartUtc = weekStartUtc,
                MonthStartUtc = monthStartUtc,
                TrailingSevenDayStartUtc = trailingSevenDayStartUtc,
                DailyCount = SumGenerations(day => day == todayUtc),
                WeeklyCount = SumGenerations(day => day >= weekStartUtc),
                MonthlyCount = SumGenerations(day => day >= monthStartUtc),
                DailyFailedCheckRetryCount = SumFailedCheckRetries(day => day == todayUtc),
                WeeklyFailedCheckRetryCount = SumFailedCheckRetries(day => day >= weekStartUtc),
                TrailingSevenDayDailyAverage = Math.Round(trailingSevenDayTotal / 7m, 2, MidpointRounding.AwayFromZero)
            };
        }
    }

    public record GenerationUsageSnapshot
    {
        public DateOnly DayUtc { get; init; }
        public DateOnly WeekStartUtc { get; init; }
        public DateOnly MonthStartUtc { get; init; }
        public DateOnly TrailingSevenDayStartUtc { get; init; }
        public long DailyCount { get; init; }
        public long WeeklyCount { get; init; }
        public long MonthlyCount { get; init; }
        public long DailyFailedCheckRetryCount { get; init; }
        public long WeeklyFailedCheckRetryCount { get; init; }
        public decimal TrailingSevenDayDailyAverage { get; init; }
    }
}
