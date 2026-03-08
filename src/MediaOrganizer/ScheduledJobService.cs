using NCrontab;

namespace MediaOrganizer;

public class ScheduledJobService : BackgroundService
{
    private readonly ILogger<ScheduledJobService> _logger;
    private readonly JobExecutor _jobExecutor;
    private readonly CrontabSchedule _schedule;
    private DateTime _nextRun;

    public ScheduledJobService(
        ILogger<ScheduledJobService> logger,
        JobExecutor jobExecutor)
    {
        _logger = logger;
        _jobExecutor = jobExecutor;
        
        // Schedule for 5:00 AM every day
        // Cron format: "minute hour day month dayofweek"
        _schedule = CrontabSchedule.Parse("0 5 * * *");
        _nextRun = _schedule.GetNextOccurrence(DateTime.Now);
        
        _logger.LogInformation("Scheduled job initialized. Next run: {NextRun}", _nextRun);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var timeUntilNextRun = _nextRun - now;

            if (timeUntilNextRun <= TimeSpan.Zero)
            {
                // Time to run the job
                _logger.LogInformation("Executing scheduled job at {Time}", now);
                
                try
                {
                    await _jobExecutor.ExecuteJobAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled job failed");
                }

                // Calculate next run time
                _nextRun = _schedule.GetNextOccurrence(DateTime.Now);
                _logger.LogInformation("Next scheduled run: {NextRun}", _nextRun);
            }
            else
            {
                // Log countdown every hour
                if (timeUntilNextRun.TotalMinutes > 60)
                {
                    _logger.LogInformation("Next job in {Hours:F1} hours at {NextRun}", 
                        timeUntilNextRun.TotalHours, _nextRun);
                }
                
                // Wait for a minute before checking again
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    public override Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled job service is stopping");
        return base.StopAsync(stoppingToken);
    }
}
