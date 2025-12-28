using Wbskt.Common;
using Wbskt.Common.Extensions;
using Wbskt.Socket.Api.HostedServices;
using Wbskt.Socket.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.ConfigureCommonServices();
builder.Services.AddSingleton<IClientConnectionManager, ClientConnectionManager>();

builder.Services.AddSingleton<SendCommandToClientEventHandler>();

builder.Services.AddAuthentication(opt =>
    {
        opt.DefaultAuthenticateScheme = Constants.AuthSchemes.ClientScheme;
        opt.DefaultChallengeScheme = Constants.AuthSchemes.ClientScheme;
    })
    .AddClientAuthScheme(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHostedService<CommandListenerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseWebSockets();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
