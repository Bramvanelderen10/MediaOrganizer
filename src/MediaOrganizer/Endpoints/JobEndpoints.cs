using MediaOrganizer.Orchestration;

namespace MediaOrganizer.Endpoints;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/trigger-job", async (JobExecutor jobExecutor, TriggerJobRequest? request) =>
        {
            try
            {
                var result = await jobExecutor.ExecuteJobAsync(request?.FolderPath);
                return Results.Ok(new
                {
                    message = "Job triggered successfully",
                    executedAt = DateTime.Now,
                    result
                });
            }
            catch (JobAlreadyRunningException ex)
            {
                return Results.Json(
                    new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("TriggerJob")
        .WithSummary("Triggers the media organization job immediately")
        .WithDescription("Optionally accepts a custom folder path. If omitted, configured defaults are used.");
    }
}
