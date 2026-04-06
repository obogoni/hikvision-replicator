using HikvisionReplicator.Api.Features.Devices;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IEncryptionService, EncryptionService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapDevicesEndpoints();

app.Run();

public partial class Program { }
