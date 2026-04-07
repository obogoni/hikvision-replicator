namespace HikvisionReplicator.Api.Features.Devices.DeleteDevice;

public interface IDeleteDeviceService
{
    Task<IResult> ExecuteAsync(int id);
}
