using GameNightApp.Components;
using Polly;
using Polly.Retry;
using Polly.RateLimiting;
using System.Threading.RateLimiting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

static ResiliencePipeline<HttpResponseMessage> CreateBggPipeline()
{
    return new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRateLimiter(new TokenBucketRateLimiter(
            new TokenBucketRateLimiterOptions
            {
                ReplenishmentPeriod = TimeSpan.FromSeconds(.1),
                AutoReplenishment = true,
                TokenLimit = 1,
                TokensPerPeriod = 1,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }))
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3,
            DelayGenerator = args =>
            {
                TimeSpan delay = TimeSpan.FromSeconds(5 * args.AttemptNumber);
                return ValueTask.FromResult<TimeSpan?>(delay);
            },
            ShouldHandle = args =>
                args.Outcome switch
                {
                    { Exception: HttpRequestException } => PredicateResult.True(),
                    { Result.StatusCode: >= System.Net.HttpStatusCode.InternalServerError } => PredicateResult.True(),
                    { Result.StatusCode: System.Net.HttpStatusCode.TooManyRequests } => PredicateResult.True(),
                    _ => PredicateResult.False()
                },
            OnRetry = args =>
            {
                Console.WriteLine(
                    $"Retry {args.AttemptNumber} after {args.RetryDelay.TotalSeconds}s");
                return ValueTask.CompletedTask;
            }
        })
        .Build();
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<BggApiService>(client =>
{
    client.BaseAddress = new Uri("https://boardgamegeek.com/xmlapi2/");
    client.DefaultRequestHeaders.Add("User-Agent", "BggGameNightPlanner");
})
.AddResilienceHandler("bgg-pipeline", builder =>
{
    builder.AddPipeline(CreateBggPipeline());
});
WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
