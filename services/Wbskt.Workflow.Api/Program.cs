using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseAuthorization();

app.MapControllers();

app.Run();
