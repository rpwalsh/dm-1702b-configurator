using System.Collections.ObjectModel;
using Bao1702.Desktop.Commands;
using Bao1702.Transport.Abstractions;

namespace Bao1702.Desktop.ViewModels;

/// <summary>View model for the device enumeration and selection panel.</summary>
public sealed class DevicePickerViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<TransportEndpoint>>> _enumerateDevices;
    private TransportEndpoint? _selectedEndpoint;
    private bool _isScanning;

    public DevicePickerViewModel(Func<CancellationToken, Task<IReadOnlyList<TransportEndpoint>>> enumerateDevices)
    {
        _enumerateDevices = enumerateDevices ?? throw new ArgumentNullException(nameof(enumerateDevices));
        ScanCommand = new AsyncRelayCommand(ScanAsync);
    }

    public ObservableCollection<TransportEndpoint> Endpoints { get; } = [];

    public TransportEndpoint? SelectedEndpoint
    {
        get => _selectedEndpoint;
        set => SetProperty(ref _selectedEndpoint, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    public AsyncRelayCommand ScanCommand { get; }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        IsScanning = true;
        try
        {
            var devices = await _enumerateDevices(cancellationToken).ConfigureAwait(true);
            Endpoints.Clear();
            foreach (var device in devices)
            {
                Endpoints.Add(device);
            }

            SelectedEndpoint = Endpoints.FirstOrDefault();
        }
        finally
        {
            IsScanning = false;
        }
    }
}
