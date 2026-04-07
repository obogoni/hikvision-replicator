using HikvisionReplicator.Api.Features.Devices.CreateDevice;
using HikvisionReplicator.Api.Features.Devices.DeleteDevice;
using HikvisionReplicator.Api.Features.Devices.GetDevice;
using HikvisionReplicator.Api.Features.Devices.GetDevices;
using HikvisionReplicator.Api.Features.Devices.UpdateDevice;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IEncryptionService, EncryptionService>();

builder
    .UseCreateDevice()
    .UseGetDevice()
    .UseGetDevices()
    .UseUpdateDevice()
    .UseDeleteDevice();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app
    .MapCreateDevice()
    .MapGetDevices()
    .MapGetDevice()
    .MapUpdateDevice()
    .MapDeleteDevice();

app.Run();

public partial class Program { }
