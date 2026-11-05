namespace LabsApp.IntegrationTests.B02.Infrastructure;

/// <summary>
/// Порты для изолированных экземпляров приложения: захват свободного порта
/// через временный TcpListener (по образцу зоны B-01).
/// </summary>
public static class Ports
{
    public static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
