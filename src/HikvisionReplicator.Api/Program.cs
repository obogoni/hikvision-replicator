using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Features.Devices.CreateDevice;
using HikvisionReplicator.Api.Features.Devices.DeleteDevice;
using HikvisionReplicator.Api.Features.Devices.GetDevice;
using HikvisionReplicator.Api.Features.Devices.GetDevices;
using HikvisionReplicator.Api.Features.Devices.UpdateDevice;
using HikvisionReplicator.Api.Features.Users.CreateUser;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IRepository<Device>, DeviceRepository>();
builder.Services.AddScoped<IRepository<User>, UserRepository>();

builder
    .UseCreateDevice()
    .UseGetDevice()
    .UseGetDevices()
    .UseUpdateDevice()
    .UseDeleteDevice()
    .UseCreateUser();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapCreateDevice()
    .MapGetDevices()
    .MapGetDevice()
    .MapUpdateDevice()
    .MapDeleteDevice()
    .MapCreateUser();

app.Run();

public partial class Program { }
