using BlueType.Agent.Models;
using BlueType.Agent.Infrastructure.Persistence;

namespace BlueType.Agent.Domain.Devices;

internal sealed class DeviceRegistry
{
    private readonly object _gate = new();
    private readonly IAuthorizedDeviceRepository _repository;
    private readonly Dictionary<string, DeviceAuthInfo> _devices;

    public DeviceRegistry(string? settingsFilePath = null)
        : this(new JsonAuthorizedDeviceRepository(
            string.IsNullOrWhiteSpace(settingsFilePath)
                ? AppSettingsStore.SettingsFilePath
                : settingsFilePath))
    {
    }

    internal DeviceRegistry(IAuthorizedDeviceRepository repository)
    {
        _repository = repository;
        _devices = _repository.Load().ToDictionary(device => device.DeviceId, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string deviceId, out DeviceAuthInfo device)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(deviceId, out device!);
        }
    }

    public IReadOnlyList<DeviceAuthInfo> GetAll()
    {
        lock (_gate)
        {
            return _devices.Values.OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public void Upsert(DeviceAuthInfo device)
    {
        lock (_gate)
        {
            var snapshot = new Dictionary<string, DeviceAuthInfo>(_devices, StringComparer.OrdinalIgnoreCase)
            {
                [device.DeviceId] = device,
            };

            _repository.Save(snapshot.Values);
            _devices.Clear();
            foreach (var entry in snapshot)
            {
                _devices[entry.Key] = entry.Value;
            }
        }
    }
}
