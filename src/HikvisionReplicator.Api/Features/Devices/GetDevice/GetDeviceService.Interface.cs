namespace HikvisionReplicator.Api.Features.Devices.GetDevice;

public interface IGetDeviceService
{
    Task<IResult> ExecuteAsync(int id);
}
