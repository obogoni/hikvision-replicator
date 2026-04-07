namespace HikvisionReplicator.Api.Features.Devices.GetDevices;

public interface IGetDevicesService
{
    Task<IResult> ExecuteAsync();
}
