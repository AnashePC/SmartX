namespace SmartX.Shared.Models;

/// <summary>
/// Buffers raw historical samples per device before they are validated and
/// transferred into the optimised List&lt;T&gt; collection the dashboard reads
/// from. Backed by a JAGGED array (double[][]) rather than a rectangular one
/// because every ESP32 node reports at its own duty cycle — some devices will
/// have buffered far more raw samples than others at any given moment, and a
/// rectangular array would force wasted padding to the longest row.
/// </summary>
public sealed class TelemetryBatchBuffer
{
    private readonly string[] _deviceIds;
    private double[][] _rawBatches; // jagged: row = device, each row's length is independent

    public IReadOnlyList<string> DeviceIds => _deviceIds;

    public TelemetryBatchBuffer(string[] deviceIds)
    {
        _deviceIds = deviceIds;
        _rawBatches = new double[deviceIds.Length][];
        for (var i = 0; i < deviceIds.Length; i++)
        {
            _rawBatches[i] = Array.Empty<double>();
        }
    }

    /// <summary>Appends samples for one device row without disturbing any other row's length.</summary>
    public void AppendBatch(int deviceIndex, double[] samples)
    {
        if (deviceIndex < 0 || deviceIndex >= _rawBatches.Length)
            throw new ArgumentOutOfRangeException(nameof(deviceIndex), "No device registered at that index.");

        var existing = _rawBatches[deviceIndex];
        var merged = new double[existing.Length + samples.Length];
        Array.Copy(existing, merged, existing.Length);
        Array.Copy(samples, 0, merged, existing.Length, samples.Length);
        _rawBatches[deviceIndex] = merged;
    }

    /// <summary>
    /// Flattens the jagged raw buffer into strongly-typed generic packets,
    /// ready for the List&lt;T&gt; the dashboard binds to.
    /// </summary>
    public List<TelemetryPacket<float>> FlattenToPackets(string unit = "")
    {
        var result = new List<TelemetryPacket<float>>();

        for (var d = 0; d < _rawBatches.Length; d++)
        {
            var row = _rawBatches[d];
            for (var s = 0; s < row.Length; s++)
            {
                result.Add(new TelemetryPacket<float>(_deviceIds[d], (float)row[s], TelemetryValueKind.Float, unit));
            }
        }

        return result;
    }

    /// <summary>Row lengths, exposed mainly so the report/demo can show the buffer is genuinely jagged.</summary>
    public int[] RowLengths() => _rawBatches.Select(row => row.Length).ToArray();
}

/// <summary>
/// Rectangular (true multi-dimensional) representation of the physical mesh
/// layout: rows = zones, columns = slot position within a zone. Seeded now
/// so the Network Topology &amp; Mesh Routing screen (final PoE) has a model
/// to build against.
/// </summary>
public sealed class MeshTopologyGrid
{
    private readonly string?[,] _grid;

    public int Zones { get; }
    public int SlotsPerZone { get; }

    public MeshTopologyGrid(int zones, int slotsPerZone)
    {
        Zones = zones;
        SlotsPerZone = slotsPerZone;
        _grid = new string?[zones, slotsPerZone];
    }

    public void PlaceDevice(int zone, int slot, string deviceId)
    {
        if (zone < 0 || zone >= Zones || slot < 0 || slot >= SlotsPerZone)
            throw new ArgumentOutOfRangeException(nameof(zone), "Zone/slot outside the mesh grid bounds.");

        _grid[zone, slot] = deviceId;
    }

    public string? DeviceAt(int zone, int slot) => _grid[zone, slot];

    public IEnumerable<(int Zone, int Slot, string DeviceId)> AllPlacedDevices()
    {
        for (var z = 0; z < Zones; z++)
        {
            for (var s = 0; s < SlotsPerZone; s++)
            {
                if (_grid[z, s] is { } id)
                {
                    yield return (z, s, id);
                }
            }
        }
    }
}
