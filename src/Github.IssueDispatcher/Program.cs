using IssueLabeler.Shared;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddSingleton<GitHubClientFactory>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IQueueHelper, QueueHelper>();
builder.Services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
builder.Services.AddSingleton<IDiffHelper, DiffHelper>();
builder.Services.AddSingleton<ILabeler, Labeler>();
builder.Services.AddAzureClients(
    factoryBuilder =>
    {
        factoryBuilder.AddBlobServiceClient(builder.Configuration["QConnectionString"]);
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
